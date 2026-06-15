import cv2
import json
import asyncio
import websockets
import mediapipe as mp
import threading
import time
from collections import deque, Counter

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

left_history = deque(maxlen=7)
right_history = deque(maxlen=7)

mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils

hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=2,
    model_complexity=1,
    min_detection_confidence=0.75,
    min_tracking_confidence=0.70
)


def finger_up(lm, tip, pip):
    return lm[tip].y < lm[pip].y - 0.015


def detect_gesture(hand_landmarks):
    lm = hand_landmarks.landmark

    index_up = finger_up(lm, 8, 6)
    middle_up = finger_up(lm, 12, 10)
    ring_up = finger_up(lm, 16, 14)
    pinky_up = finger_up(lm, 20, 18)

    fingers_up = [index_up, middle_up, ring_up, pinky_up]
    total_up = sum(fingers_up)

    # Open hand: all four main fingers clearly open
    if total_up == 4:
        return "Open Hand"

    # Victory: index + middle only
    if index_up and middle_up and not ring_up and not pinky_up:
        return "Victory"

    # One finger: index only
    if index_up and not middle_up and not ring_up and not pinky_up:
        return "One Finger"

    # Fist: all four fingers closed
    if total_up == 0:
        return "Fist"

    return "Partial Gesture"


def smooth_gesture(new_gesture, history):
    history.append(new_gesture)

    counts = Counter(history)
    gesture, count = counts.most_common(1)[0]

    # Avoid unstable partial detection
    if gesture == "Partial Gesture" and len(history) >= 2:
        for old in reversed(history):
            if old != "Partial Gesture":
                return old

    return gesture


def camera_loop():
    global latest_data

    cap = cv2.VideoCapture(0)

    if not cap.isOpened():
        print("Camera not found. Try cv2.VideoCapture(1)")
        return

    while True:
        success, frame = cap.read()

        if not success:
            break

        frame = cv2.flip(frame, 1)
        frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results = hands.process(frame_rgb)

        left_gesture = "No Hand"
        right_gesture = "No Hand"

        left_x = left_y = left_z = 0
        right_x = right_y = right_z = 0

        if results.multi_hand_landmarks and results.multi_handedness:
            for hand_landmarks, handedness in zip(
                results.multi_hand_landmarks,
                results.multi_handedness
            ):
                label = handedness.classification[0].label
                score = handedness.classification[0].score

                mp_drawing.draw_landmarks(
                    frame,
                    hand_landmarks,
                    mp_hands.HAND_CONNECTIONS
                )

                wrist = hand_landmarks.landmark[0]
                detected = detect_gesture(hand_landmarks)


                if label == "Left":
                    left_gesture = smooth_gesture(detected, left_history)
                    left_x = round(wrist.x, 3)
                    left_y = round(wrist.y, 3)
                    left_z = round(wrist.z, 3)

                    cv2.putText(
                        frame,
                        f"LEFT: {left_gesture}",
                        (20, 45),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        1,
                        (255, 255, 0),
                        2
                    )

                elif label == "Right":
                    right_gesture = smooth_gesture(detected, right_history)
                    right_x = round(wrist.x, 3)
                    right_y = round(wrist.y, 3)
                    right_z = round(wrist.z, 3)

                    cv2.putText(
                        frame,
                        f"RIGHT: {right_gesture}",
                        (20, 90),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        1,
                        (0, 255, 0),
                        2
                    )

        else:
            left_history.clear()
            right_history.clear()

        latest_data = {
            "leftGesture": left_gesture,
            "rightGesture": right_gesture,
            "leftX": left_x,
            "leftY": left_y,
            "leftZ": left_z,
            "rightX": right_x,
            "rightY": right_y,
            "rightZ": right_z,
            "timestamp": round(time.time(), 2)
        }

        cv2.imshow("Space Rover Dual Hand Sensor", frame)

        if cv2.waitKey(1) & 0xFF == 27:
            break

    cap.release()
    cv2.destroyAllWindows()


async def send_sensor_data(websocket):
    print("Unity connected")

    try:
        while True:
            await websocket.send(json.dumps(latest_data))
            await asyncio.sleep(0.05)
    except websockets.exceptions.ConnectionClosed:
        print("Unity disconnected")


async def main():
    server = await websockets.serve(send_sensor_data, "localhost", 8765)
    print("Python sensor running at ws://localhost:8765")
    print("ESC = close camera")
    await server.wait_closed()


threading.Thread(target=camera_loop, daemon=True).start()
asyncio.run(main())