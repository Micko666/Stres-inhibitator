using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Manages the memory puzzle sequence (show sequence → player repeats)
public class PuzzleManager : MonoBehaviour
{
    public static PuzzleManager Instance { get; private set; }

    [Header("Puzzle Config")]
    public int sequenceLength = 5;
    public float showStepDuration = 1.0f;
    public float pauseBetweenSteps = 0.5f;

    [Header("Puzzle Buttons (assign in Inspector)")]
    public PuzzleButton[] buttons;

    [Header("Events")]
    public UnityEvent onSequenceComplete;
    public UnityEvent onPuzzleFailed;
    public UnityEvent onPuzzleSolved;

    [Header("Hint System")]
    public int maxHints = 3;

    private List<int> sequence = new List<int>();
    private int playerIndex = 0;
    private int hintsUsed = 0;
    private bool playerTurn = false;
    private bool solved = false;

    public bool IsSolved => solved;
    public int HintsUsed => hintsUsed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void GenerateAndShowSequence()
    {
        sequence.Clear();
        for (int i = 0; i < sequenceLength; i++)
            sequence.Add(Random.Range(0, buttons.Length));

        playerIndex = 0;
        playerTurn = false;
        solved = false;
        hintsUsed = 0;

        StartCoroutine(ShowSequence());
    }

    IEnumerator ShowSequence()
    {
        SetButtonsInteractable(false);
        yield return new WaitForSeconds(1f);

        foreach (int idx in sequence)
        {
            buttons[idx].Highlight(true);
            yield return new WaitForSeconds(showStepDuration);
            buttons[idx].Highlight(false);
            yield return new WaitForSeconds(pauseBetweenSteps);
        }

        SetButtonsInteractable(true);
        playerTurn = true;
        onSequenceComplete?.Invoke(); // fires AFTER sequence is fully shown, player can now press
    }

    public void OnPlayerPressedButton(int index)
    {
        if (!playerTurn || solved) return;

        if (index == sequence[playerIndex])
        {
            playerIndex++;
            if (playerIndex >= sequence.Count)
            {
                solved = true;
                playerTurn = false;
                SetButtonsInteractable(false);
                onPuzzleSolved?.Invoke();
            }
        }
        else
        {
            playerTurn = false;
            SetButtonsInteractable(false);
            onPuzzleFailed?.Invoke();
            // Replay sequence after short delay
            Invoke(nameof(RestartPlayerTurn), 2f);
        }
    }

    public void UseHint()
    {
        if (!playerTurn || hintsUsed >= maxHints || solved) return;
        hintsUsed++;
        StartCoroutine(ShowHint());
    }

    IEnumerator ShowHint()
    {
        SetButtonsInteractable(false);
        int idx = sequence[playerIndex];
        buttons[idx].Highlight(true);
        yield return new WaitForSeconds(0.8f);
        buttons[idx].Highlight(false);
        SetButtonsInteractable(true);
    }

    void RestartPlayerTurn()
    {
        playerIndex = 0;
        StartCoroutine(ShowSequence());
    }

    void SetButtonsInteractable(bool state)
    {
        foreach (var b in buttons)
            b.SetInteractable(state);
    }
}
