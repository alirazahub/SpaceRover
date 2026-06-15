# Space Rover Gesture Controller — AI4CI Final Project
### Muhammad | June 2026

---

## Project Summary

A real-time gesture-controlled Mars Rover simulator using:
- **Python** + **MediaPipe** for dual-hand gesture detection
- **WebSocket** as the communication protocol
- **Unity 6** for the 3D simulation environment + live HUD

---

## Repository Structure

```
SourceCode/
├── Python/
│   └── gesture_sensor.py          ← Run this first
└── Unity/
    └── Scripts/
        └── SpaceRoverController.cs ← Attach to rover GameObject in Unity
```

---

## Quick Start

### Step 1 — Install Python dependencies
```bash
pip install mediapipe opencv-python websockets
```

### Step 2 — Run the sensor
```bash
python gesture_sensor.py
```
You should see the camera window open with landmark overlay.

### Step 3 — Open Unity
- Import **NativeWebSocket** from the Unity Asset Store (or GitHub)
- Attach `SpaceRoverController.cs` to your rover GameObject
- Wire up all Inspector references (wheels, HUD TextMeshPro fields, particles)
- Press **Play** — rover auto-connects to `ws://localhost:8765`

---

## Gesture Reference

| Gesture     | Hand  | Action               |
|-------------|-------|----------------------|
| Open Hand   | Left  | Move Forward         |
| Fist        | Left  | Stop                 |
| Victory (V) | Left  | Turn Left            |
| One Finger  | Left  | Turn Right           |
| Victory (V) | Right | Reverse Mode (hold)  |
| Open Hand   | Right | Speed Up +0.3 m/s    |
| Fist        | Right | Speed Down -0.3 m/s  |

---

## Communication Protocol Details

- **Protocol:** WebSocket (RFC 6455)
- **Address:** `ws://localhost:8765`
- **Frequency:** 20Hz (every 50ms)
- **Format:** JSON, 9 fields per message
- **Fields:** leftGesture, rightGesture, leftX/Y/Z, rightX/Y/Z, timestamp

---

## Video Demo
https://www.loom.com/share/3d1ed5d228dd4f9fbb51852353527500

---

## Technical Report
See: `TechnicalReport_SpaceRover.docx`
