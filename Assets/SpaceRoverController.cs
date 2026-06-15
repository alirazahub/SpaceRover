using UnityEngine;
using NativeWebSocket;
using TMPro;

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

public class SpaceRoverController : MonoBehaviour
{
    private WebSocket websocket;

    [Header("UI")]
    public TextMeshProUGUI leftGestureText;
    public TextMeshProUGUI rightGestureText;
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI batteryText;
    public TextMeshProUGUI temperatureText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI coordinatesText;
    public TextMeshProUGUI directionText;

    [Header("Rover Objects")]
    public Transform rover;
    public Transform[] wheels;

    [Header("Effects")]
    public Light headLight;
    public ParticleSystem dustParticles;
    public ParticleSystem drillParticles;

    [Header("Movement Settings")]
    public float moveSpeed = 2f;
    public float turnSpeed = 70f;
    public float wheelRotationSpeed = 300f;

    private float currentSpeed = 0f;
    private float battery = 100f;
    private float temperature = 25f;

    private bool reverseMode = false;
    private string roverStatus = "Idle";

    private float speedChangeCooldown = 0f;

    async void Start()
    {
        websocket = new WebSocket("ws://localhost:8765");

        websocket.OnOpen += () =>
        {
            Debug.Log("Connected to Python gesture sensor");
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
            Debug.Log("WebSocket Error: " + error);
            roverStatus = "Connection Error";
        };

        websocket.OnClose += (code) =>
        {
            Debug.Log("WebSocket Closed");
            roverStatus = "Disconnected";
        };

        await websocket.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif

        speedChangeCooldown -= Time.deltaTime;

        UpdateRoverMovement();
        UpdateRoverTelemetry();
        UpdateWheelRotation();
        UpdateUI();
    }

    void HandleSensorData(RoverSensorData data)
    {
        if (leftGestureText != null)
            leftGestureText.text = "Left Hand: " + data.leftGesture;

        if (rightGestureText != null)
            rightGestureText.text = "Right Hand: " + data.rightGesture;

        if (coordinatesText != null)
        {
            coordinatesText.text =
                "Left Wrist: x=" + data.leftX + " y=" + data.leftY + " z=" + data.leftZ +
                "\nRight Wrist: x=" + data.rightX + " y=" + data.rightY + " z=" + data.rightZ;
        }

        HandleLeftHandMovement(data.leftGesture);
        HandleRightHandTools(data.rightGesture);
    }

    void HandleLeftHandMovement(string gesture)
    {
        switch (gesture)
        {
            case "Open Hand":
                currentSpeed = moveSpeed;

                roverStatus = reverseMode
                    ? "Moving Reverse"
                    : "Moving Forward";

                if (dustParticles != null && !dustParticles.isPlaying)
                    dustParticles.Play();

                break;

            case "Fist":
                currentSpeed = 0f;
                roverStatus = "Stopped";

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
                currentSpeed = 0f;

                if (dustParticles != null && dustParticles.isPlaying)
                    dustParticles.Stop();

                break;
        }
    }

    void HandleRightHandTools(string gesture)
    {
        // Hold Right Victory = reverse mode
        // Release Right Victory = forward mode
        reverseMode = gesture == "Victory";

        if (speedChangeCooldown > 0)
            return;

        if (gesture == "Open Hand")
        {
            moveSpeed += 0.3f;
            moveSpeed = Mathf.Clamp(moveSpeed, 0f, 8f);
            speedChangeCooldown = 0.5f;
            roverStatus = "Speed Increased";
        }
        else if (gesture == "Fist")
        {
            moveSpeed -= 0.3f;
            moveSpeed = Mathf.Clamp(moveSpeed, 0f, 8f);
            speedChangeCooldown = 0.5f;
            roverStatus = "Speed Decreased";
        }
    }

    void UpdateRoverMovement()
    {
        if (rover == null)
            return;

        if (currentSpeed > 0)
        {
            Vector3 direction = reverseMode ? Vector3.back : Vector3.forward;
            rover.Translate(direction * currentSpeed * Time.deltaTime);
        }
    }

    void UpdateWheelRotation()
    {
        if (wheels == null)
            return;

        float directionMultiplier = reverseMode ? -1f : 1f;

        foreach (Transform wheel in wheels)
        {
            if (wheel != null)
            {
                wheel.Rotate(
                    Vector3.right,
                    directionMultiplier * currentSpeed * wheelRotationSpeed * Time.deltaTime
                );
            }
        }
    }

    void UpdateRoverTelemetry()
    {
        if (currentSpeed > 0)
        {
            battery -= Time.deltaTime * 0.5f;
            temperature += Time.deltaTime * 0.2f;
        }
        else
        {
            temperature -= Time.deltaTime * 0.1f;
        }

        battery = Mathf.Clamp(battery, 0f, 100f);
        temperature = Mathf.Clamp(temperature, 20f, 100f);

        if (battery <= 10f)
        {
            roverStatus = "Low Battery Warning";
        }
    }

    void UpdateUI()
    {
        if (speedText != null)
            speedText.text = "Speed: " + moveSpeed.ToString("F1") + " m/s";

        if (batteryText != null)
            batteryText.text = "Battery: " + battery.ToString("F0") + "%";

        if (temperatureText != null)
            temperatureText.text = "Temperature: " + temperature.ToString("F1") + " °C";

        if (statusText != null)
            statusText.text = "Status: " + roverStatus;

        if (directionText != null)
            directionText.text = reverseMode ? "Direction: Reverse" : "Direction: Forward";
    }

    async void OnApplicationQuit()
    {
        if (websocket != null)
        {
            await websocket.Close();
        }
    }
}