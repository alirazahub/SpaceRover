/*
=============================================================================
  SpaceRoverController.cs
  Ali Raza — Integration of Virtual and Augmented Reality Technologies in Connected Industries Final Project — June 2026
=============================================================================

  DESCRIPTION:
    Unity 3D MonoBehaviour that connects to the Python gesture sensor
    over WebSocket and translates real-time hand gesture data into
    rover movement, tool activation, and HUD visualization.

  COMMUNICATION PROTOCOL: WebSocket (NativeWebSocket package)
  DATA SOURCE: Python gesture_sensor.py on ws://localhost:8765

  DEPENDENCIES:
    - NativeWebSocket (Unity Asset Store or GitHub: endel/NativeWebSocket)
    - TextMeshPro (Unity built-in, install via Package Manager)

  SETUP IN UNITY:
    1. Attach this script to a GameObject (e.g., "RoverController")
    2. Assign all public fields in the Inspector:
       - Drag the rover Transform
       - Drag all 4 wheel Transforms
       - Drag all TextMeshProUGUI text objects for HUD
       - Drag particle system and headlight references
    3. Launch Python sensor first, then press Play in Unity
    4. The rover auto-connects — check Console for "[WS] Connected" log

  GESTURE CONTROL MAP:
    LEFT HAND:
      Open Hand   → Move Forward (at current moveSpeed)
      Fist        → Stop
      Victory (V) → Turn Left
      One Finger  → Turn Right

    RIGHT HAND:
      Victory (V) → Hold to enable Reverse mode
      Open Hand   → Increase speed +0.3 m/s (max 8, cooldown 0.5s)
      Fist        → Decrease speed -0.3 m/s (min 0, cooldown 0.5s)

=============================================================================
*/

using UnityEngine;
using NativeWebSocket;
using TMPro;

/// <summary>
/// Data contract matching the Python sensor JSON payload.
/// All 9 fields are deserialized by JsonUtility per message.
/// </summary>
[System.Serializable]
public class RoverSensorData
{
    public string leftGesture;
    public string rightGesture;

    public float leftX;
    public float leftY;
    public float leftZ;

    public float rightX;
    public float rightY;
    public float rightZ;

    public float timestamp;
}

/// <summary>
/// Main rover controller. Receives gesture data from Python via WebSocket
/// and maps them to: movement, tool systems, and real-time HUD telemetry.
/// </summary>
public class SpaceRoverController : MonoBehaviour
{
    // ── WebSocket ──────────────────────────────────────────────────────────────
    private WebSocket websocket;

    // ── HUD Text Fields (assign in Inspector) ─────────────────────────────────
    [Header("UI / HUD")]
    public TextMeshProUGUI leftGestureText;     // Displays current left hand gesture
    public TextMeshProUGUI rightGestureText;    // Displays current right hand gesture
    public TextMeshProUGUI speedText;           // Current move speed (m/s)
    public TextMeshProUGUI batteryText;         // Simulated battery percentage
    public TextMeshProUGUI temperatureText;     // Simulated core temperature (°C)
    public TextMeshProUGUI statusText;          // Current rover status string
    public TextMeshProUGUI coordinatesText;     // Wrist X/Y/Z coordinates
    public TextMeshProUGUI directionText;       // Forward / Reverse indicator

    // ── Scene Objects (assign in Inspector) ──────────────────────────────────
    [Header("Rover Objects")]
    public Transform rover;         // Root rover Transform (moves + rotates)
    public Transform[] wheels;      // Wheel Transforms (spin on X axis)

    // ── Effects (assign in Inspector) ─────────────────────────────────────────
    [Header("Effects")]
    public Light headLight;                  // Rover headlight
    public ParticleSystem dustParticles;     // Surface dust (plays while moving)
    public ParticleSystem drillParticles;    // Drill tool (triggered by tools gesture)

    // ── Tunable Parameters ────────────────────────────────────────────────────
    [Header("Movement Settings")]
    public float moveSpeed          = 2f;    // Current top speed (m/s), modified by right hand
    public float turnSpeed          = 70f;   // Degrees/sec rotation rate
    public float wheelRotationSpeed = 300f;  // Visual wheel spin multiplier

    // ── Runtime State ─────────────────────────────────────────────────────────
    private float currentSpeed      = 0f;    // Actual speed this frame
    private float battery           = 100f;  // Simulated battery %
    private float temperature       = 25f;   // Simulated temperature °C

    private bool  reverseMode       = false; // True when right Victory is held
    private string roverStatus      = "Idle";

    private float speedChangeCooldown = 0f;  // Prevents rapid speed toggling


    // ── Lifecycle ─────────────────────────────────────────────────────────────

    async void Start()
    {
        websocket = new WebSocket("ws://localhost:8765");

        websocket.OnOpen += () =>
        {
            Debug.Log("[WS] Connected to Python gesture sensor");
            roverStatus = "Connected";
        };

        websocket.OnMessage += (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
            RoverSensorData data = JsonUtility.FromJson<RoverSensorData>(message);
            HandleSensorData(data);
        };

        websocket.OnError += (error) =>
        {
            Debug.Log("[WS] Error: " + error);
            roverStatus = "Connection Error";
        };

        websocket.OnClose += (code) =>
        {
            Debug.Log("[WS] Disconnected (code: " + code + ")");
            roverStatus = "Disconnected";
        };

        await websocket.Connect();
    }

    void Update()
    {
        // NativeWebSocket requires manual dispatch on non-WebGL platforms
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif

        speedChangeCooldown -= Time.deltaTime;

        UpdateRoverMovement();
        UpdateRoverTelemetry();
        UpdateWheelRotation();
        UpdateUI();
    }


    // ── Sensor Data Handling ──────────────────────────────────────────────────

    /// <summary>
    /// Entry point for each WebSocket message.
    /// Updates HUD labels and dispatches to gesture handlers.
    /// </summary>
    void HandleSensorData(RoverSensorData data)
    {
        // ── Raw gesture labels on HUD
        if (leftGestureText  != null) leftGestureText.text  = "Left Hand: "  + data.leftGesture;
        if (rightGestureText != null) rightGestureText.text = "Right Hand: " + data.rightGesture;

        // ── Wrist position readout
        if (coordinatesText != null)
        {
            coordinatesText.text =
                $"Left Wrist:  x={data.leftX}  y={data.leftY}  z={data.leftZ}\n" +
                $"Right Wrist: x={data.rightX}  y={data.rightY}  z={data.rightZ}";
        }

        HandleLeftHandMovement(data.leftGesture);
        HandleRightHandTools(data.rightGesture);
    }


    // ── Left Hand: Movement Control ───────────────────────────────────────────

    /// <summary>
    /// Maps left-hand gestures to rover locomotion.
    /// Open Hand = drive, Fist = brake, Victory = left, One Finger = right.
    /// </summary>
    void HandleLeftHandMovement(string gesture)
    {
        switch (gesture)
        {
            case "Open Hand":
                currentSpeed = moveSpeed;
                roverStatus  = reverseMode ? "Moving Reverse" : "Moving Forward";

                if (dustParticles != null && !dustParticles.isPlaying)
                    dustParticles.Play();
                break;

            case "Fist":
                currentSpeed = 0f;
                roverStatus  = "Stopped";

                if (dustParticles != null && dustParticles.isPlaying)
                    dustParticles.Stop();
                break;

            case "Victory":
                currentSpeed = moveSpeed * 0.5f;

                if (rover != null)
                    rover.Rotate(Vector3.up, -turnSpeed * Time.deltaTime);

                roverStatus = "Turning Left";

                if (dustParticles != null && !dustParticles.isPlaying)
                    dustParticles.Play();
                break;

            case "One Finger":
                currentSpeed = moveSpeed * 0.5f;

                if (rover != null)
                    rover.Rotate(Vector3.up, turnSpeed * Time.deltaTime);

                roverStatus = "Turning Right";

                if (dustParticles != null && !dustParticles.isPlaying)
                    dustParticles.Play();
                break;

            default:
                // Unknown or "No Hand" — halt and clean up
                currentSpeed = 0f;

                if (dustParticles != null && dustParticles.isPlaying)
                    dustParticles.Stop();
                break;
        }
    }


    // ── Right Hand: Speed and Mode Control ───────────────────────────────────

    /// <summary>
    /// Maps right-hand gestures to speed modulation and reverse toggle.
    /// Includes a 0.5s cooldown to prevent accidental rapid adjustments.
    /// </summary>
    void HandleRightHandTools(string gesture)
    {
        // Victory held = reverse mode
        reverseMode = (gesture == "Victory");

        if (speedChangeCooldown > 0f)
            return;

        if (gesture == "Open Hand")
        {
            moveSpeed             = Mathf.Clamp(moveSpeed + 0.3f, 0f, 8f);
            speedChangeCooldown   = 0.5f;
            roverStatus           = "Speed Increased";
        }
        else if (gesture == "Fist")
        {
            moveSpeed             = Mathf.Clamp(moveSpeed - 0.3f, 0f, 8f);
            speedChangeCooldown   = 0.5f;
            roverStatus           = "Speed Decreased";
        }
    }


    // ── Movement & Physics ────────────────────────────────────────────────────

    void UpdateRoverMovement()
    {
        if (rover == null || currentSpeed <= 0f) return;

        Vector3 direction = reverseMode ? Vector3.back : Vector3.forward;
        rover.Translate(direction * currentSpeed * Time.deltaTime);
    }

    void UpdateWheelRotation()
    {
        if (wheels == null) return;

        float dir = reverseMode ? -1f : 1f;

        foreach (Transform wheel in wheels)
        {
            if (wheel != null)
                wheel.Rotate(Vector3.right, dir * currentSpeed * wheelRotationSpeed * Time.deltaTime);
        }
    }


    // ── Telemetry Simulation ──────────────────────────────────────────────────

    /// <summary>
    /// Simulates realistic battery drain and thermal behavior.
    /// Battery: -0.5%/s moving. Temperature: +0.2°C/s moving, -0.1°C/s idle.
    /// </summary>
    void UpdateRoverTelemetry()
    {
        if (currentSpeed > 0f)
        {
            battery     -= Time.deltaTime * 0.5f;
            temperature += Time.deltaTime * 0.2f;
        }
        else
        {
            temperature -= Time.deltaTime * 0.1f;
        }

        battery     = Mathf.Clamp(battery,     0f,   100f);
        temperature = Mathf.Clamp(temperature, 20f,  100f);

        if (battery <= 10f)
            roverStatus = "LOW BATTERY WARNING";
    }


    // ── HUD Rendering ─────────────────────────────────────────────────────────

    /// <summary>
    /// Updates all telemetry TextMeshPro fields every frame.
    /// Called at the end of Update() after all state has been resolved.
    /// </summary>
    void UpdateUI()
    {
        if (speedText       != null) speedText.text       = $"Speed: {moveSpeed:F1} m/s";
        if (batteryText     != null) batteryText.text     = $"Battery: {battery:F0}%";
        if (temperatureText != null) temperatureText.text = $"Temperature: {temperature:F1} °C";
        if (statusText      != null) statusText.text      = $"Status: {roverStatus}";
        if (directionText   != null) directionText.text   = reverseMode ? "Direction: REVERSE" : "Direction: Forward";
    }


    // ── Cleanup ───────────────────────────────────────────────────────────────

    async void OnApplicationQuit()
    {
        if (websocket != null)
            await websocket.Close();
    }
}
