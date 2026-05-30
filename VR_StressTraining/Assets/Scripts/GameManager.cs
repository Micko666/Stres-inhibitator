using UnityEngine;
using UnityEngine.Events;

public enum GamePhase { Calibration, CalmRoom, StressRoom, Debriefing }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Phase Durations")]
    public float calibrationDuration = 30f;

    [Header("Events")]
    public UnityEvent onCalmRoomStart;
    public UnityEvent onStressRoomStart;
    public UnityEvent onDebriefingStart;

    public GamePhase CurrentPhase { get; private set; } = GamePhase.Calibration;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        StartCalibration();
    }

    void StartCalibration()
    {
        CurrentPhase = GamePhase.Calibration;
        Invoke(nameof(StartCalmRoom), calibrationDuration);
    }

    public void StartCalmRoom()
    {
        CancelInvoke();
        CurrentPhase = GamePhase.CalmRoom;
        onCalmRoomStart?.Invoke();
    }

    public void StartStressRoom()
    {
        CurrentPhase = GamePhase.StressRoom;
        onStressRoomStart?.Invoke();
    }

    public void StartDebriefing()
    {
        CurrentPhase = GamePhase.Debriefing;
        onDebriefingStart?.Invoke();
    }
}
