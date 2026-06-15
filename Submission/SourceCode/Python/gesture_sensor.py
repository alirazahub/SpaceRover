"""
=============================================================================
  SPACE ROVER DUAL HAND GESTURE SENSOR
  AI4CI Final Project — June 2026
=============================================================================

  DESCRIPTION:
    Captures dual-hand gestures in real time via webcam using MediaPipe.
    Broadcasts gesture data + wrist coordinates as JSON over WebSocket
    at 20Hz to a Unity 3D environment.

  COMMUNICATION PROTOCOL: WebSocket (ws://localhost:8765)

  DEPENDENCIES:
    pip install mediapipe opencv-python websockets

  USAGE:
    python gesture_sensor.py
    Then launch the Unity scene — it auto-connects.
    Press ESC to close the camera window.

  GESTURE REFERENCE:
    Left Hand  → Rover movement (Open=Forward, Fist=Stop, Victory=Left, 1Finger=Right)
    Right Hand → Speed/mode    (Victory=Reverse, OpenHand=SpeedUp, Fist=SpeedDown)

  JSON PAYLOAD (sent every 50ms):
    {
      "leftGesture":  "Open Hand | Fist | Victory | One Finger | No Hand",
      "rightGesture": "Open Hand | Fist | Victory | One Finger | No Hand",
      "leftX":  0.0–1.0,   "leftY":  0.0–1.0,   "leftZ": float,
      "rightX": 0.0–1.0,   "rightY": 0.0–1.0,   "rightZ": float,
      "timestamp": float (Unix seconds)
    }

=============================================================================
"""

import cv2
import json
import asyncio
import websockets
import mediapipe as mp
import threading
import time
from collections import deque, Counter

# ─── Shared State ─────────────────────────────────────────────────────────────

latest_data = {
    "leftGesture": "No Hand",
    "rightGesture": "No Hand",
    "leftX": 0,
    "leftY": 0,
    "leftZ": 0,
    "rightX": 0,
    "rightY": 0,
    "rightZ": 0,
    "timestamp": 0
}

# Temporal smoothing buffers (7 frames each hand)
left_history = deque(maxlen=7)
right_history = deque(maxlen=7)

# ─── MediaPipe Setup ──────────────────────────────────────────────────────────

mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils

hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=2,
    model_complexity=1,
    min_detection_confidence=0.75,
    min_tracking_confidence=0.70
)


# ─── Gesture Detection ────────────────────────────────────────────────────────

def finger_up(lm, tip, pip):
    """
    Returns True if a finger is extended upward.
    Uses a 0.015 threshold to suppress noise from near-horizontal fingers.
    MediaPipe Y-axis: 0 = top of frame, 1 = bottom.
    """
    return lm[tip].y < lm[pip].y - 0.015


def detect_gesture(hand_landmarks):
    """
    Classifies hand shape into one of 5 gesture categories.

    Landmarks used:
      8  = Index tip    6  = Index PIP
      12 = Middle tip   10 = Middle PIP
      16 = Ring tip     14 = Ring PIP
      20 = Pinky tip    18 = Pinky PIP

    Returns:
      "Open Hand"       — all 4 main fingers extended
      "Victory"         — index + middle only
      "One Finger"      — index only
      "Fist"            — all fingers closed
      "Partial Gesture" — ambiguous state (will be smoothed away)
    """
    lm = hand_landmarks.landmark

    index_up  = finger_up(lm, 8,  6)
    middle_up = finger_up(lm, 12, 10)
    ring_up   = finger_up(lm, 16, 14)
    pinky_up  = finger_up(lm, 20, 18)

    fingers_up = [index_up, middle_up, ring_up, pinky_up]
    total_up   = sum(fingers_up)

    if total_up == 4:
        return "Open Hand"
    if index_up and middle_up and not ring_up and not pinky_up:
        return "Victory"
    if index_up and not middle_up and not ring_up and not pinky_up:
        return "One Finger"
    if total_up == 0:
        return "Fist"

    return "Partial Gesture"


def smooth_gesture(new_gesture, history):
    """
    Temporal majority-vote smoothing over the last N frames.
    Partial Gesture degrades to the most recent stable gesture
    to prevent flickering during transitions.
    """
    history.append(new_gesture)
    counts = Counter(history)
    gesture, _ = counts.most_common(1)[0]

    if gesture == "Partial Gesture" and len(history) >= 2:
        for old in reversed(history):
            if old != "Partial Gesture":
                return old

    return gesture


# ─── Camera Loop (runs in daemon thread) ──────────────────────────────────────

def camera_loop():
    """
    Main capture + detection loop.
    Runs on a background daemon thread so it doesn't block the asyncio event loop.
    Updates the global latest_data dict on every frame.
    """
    global latest_data

    cap = cv2.VideoCapture(0)

    if not cap.isOpened():
        print("[ERROR] Camera not found. Try cv2.VideoCapture(1)")
        return

    print("[INFO] Camera opened successfully")

    while True:
        success, frame = cap.read()
        if not success:
            break

        # Mirror frame for natural interaction
        frame     = cv2.flip(frame, 1)
        frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results   = hands.process(frame_rgb)

        left_gesture  = "No Hand"
        right_gesture = "No Hand"
        left_x = left_y = left_z = 0
        right_x = right_y = right_z = 0

        if results.multi_hand_landmarks and results.multi_handedness:
            for hand_landmarks, handedness in zip(
                results.multi_hand_landmarks,
                results.multi_handedness
            ):
                label = handedness.classification[0].label

                mp_drawing.draw_landmarks(
                    frame,
                    hand_landmarks,
                    mp_hands.HAND_CONNECTIONS
                )

                wrist    = hand_landmarks.landmark[0]
                detected = detect_gesture(hand_landmarks)

                if label == "Left":
                    left_gesture = smooth_gesture(detected, left_history)
                    left_x = round(wrist.x, 3)
                    left_y = round(wrist.y, 3)
                    left_z = round(wrist.z, 3)

                    cv2.putText(frame, f"LEFT: {left_gesture}",
                                (20, 45), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 0), 2)

                elif label == "Right":
                    right_gesture = smooth_gesture(detected, right_history)
                    right_x = round(wrist.x, 3)
                    right_y = round(wrist.y, 3)
                    right_z = round(wrist.z, 3)

                    cv2.putText(frame, f"RIGHT: {right_gesture}",
                                (20, 90), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)

        else:
            # No hands detected — clear smoothing buffers
            left_history.clear()
            right_history.clear()

        # Update shared state (GIL-protected dict swap is atomic in CPython)
        latest_data = {
            "leftGesture":  left_gesture,
            "rightGesture": right_gesture,
            "leftX":  left_x,  "leftY":  left_y,  "leftZ":  left_z,
            "rightX": right_x, "rightY": right_y, "rightZ": right_z,
            "timestamp": round(time.time(), 2)
        }

        cv2.imshow("Space Rover Dual Hand Sensor", frame)

        if cv2.waitKey(1) & 0xFF == 27:  # ESC
            break

    cap.release()
    cv2.destroyAllWindows()
    print("[INFO] Camera closed")


# ─── WebSocket Server ─────────────────────────────────────────────────────────

async def send_sensor_data(websocket):
    """
    Handles a single Unity client connection.
    Streams latest_data as JSON every 50ms (20Hz).
    """
    print(f"[WS] Unity client connected: {websocket.remote_address}")

    try:
        while True:
            await websocket.send(json.dumps(latest_data))
            await asyncio.sleep(0.05)   # 20Hz broadcast
    except websockets.exceptions.ConnectionClosed:
        print(f"[WS] Unity client disconnected")


async def main():
    server = await websockets.serve(send_sensor_data, "localhost", 8765)
    print("[WS] WebSocket server running at ws://localhost:8765")
    print("[INFO] Press ESC in the camera window to quit")
    await server.wait_closed()


# ─── Entry Point ──────────────────────────────────────────────────────────────

if __name__ == "__main__":
    # Camera runs on a daemon thread — auto-exits when main thread ends
    threading.Thread(target=camera_loop, daemon=True).start()
    asyncio.run(main())
