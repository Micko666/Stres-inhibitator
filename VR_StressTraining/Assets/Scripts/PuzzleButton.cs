using UnityEngine;

// Attach to each interactable puzzle button in the scene
[RequireComponent(typeof(Renderer))]
public class PuzzleButton : MonoBehaviour
{
    [Header("Setup")]
    public int buttonIndex;
    public Color normalColor = Color.white;
    public Color highlightColor = Color.yellow;
    public Color pressedColor = Color.green;

    private Renderer rend;
    private bool interactable = false;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        rend.material.color = normalColor;
    }

    public void Highlight(bool on)
    {
        rend.material.color = on ? highlightColor : normalColor;
    }

    public void SetInteractable(bool state)
    {
        interactable = state;
        rend.material.color = state ? normalColor : Color.gray;
    }

    // Called by Meta XR Interaction SDK PokeInteractable or via raycast
    public void OnPressed()
    {
        if (!interactable) return;
        StartCoroutine(PressEffect());
        PuzzleManager.Instance?.OnPlayerPressedButton(buttonIndex);
    }

    System.Collections.IEnumerator PressEffect()
    {
        rend.material.color = pressedColor;
        yield return new WaitForSeconds(0.15f);
        rend.material.color = interactable ? normalColor : Color.gray;
    }
}
