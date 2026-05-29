using System.Collections;
using UnityEngine;

// Controls the stress room: closing walls + tiger cage opening
public class StressRoomController : MonoBehaviour
{
    [Header("Closing Walls (assign 4 wall transforms)")]
    public Transform wallNorth;
    public Transform wallSouth;
    public Transform wallEast;
    public Transform wallWest;

    [Header("Wall Movement")]
    public float wallMoveDistance = 3.0f;
    public float wallMoveDuration = 120f; // seconds until fully closed
    public AnimationCurve wallEaseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Tiger Cage")]
    public GameObject cageDoor;
    public float cageDoorOpenDelay = 10f;
    public float cageDoorOpenAngle = 90f;
    public float cageDoorOpenDuration = 2f;

    [Header("Tiger")]
    public Animator tigerAnimator;
    public Transform tigerPatrolTarget;
    public float tigerActivateDelay = 15f;

    [Header("Audio")]
    public AudioSource ambientAudio;
    public AudioClip tigerRoarClip;
    public AudioClip wallCreakClip;

    private Vector3 wallNorthStart, wallSouthStart, wallEastStart, wallWestStart;
    private bool active = false;
    private bool creak33Played = false;
    private bool creak66Played = false;

    void Start()
    {
        if (wallNorth) wallNorthStart = wallNorth.localPosition;
        if (wallSouth) wallSouthStart = wallSouth.localPosition;
        if (wallEast)  wallEastStart  = wallEast.localPosition;
        if (wallWest)  wallWestStart  = wallWest.localPosition;
    }

    public void Activate()
    {
        if (active) return;
        active = true;
        creak33Played = false;
        creak66Played = false;
        StartCoroutine(MoveWalls());
        Invoke(nameof(OpenCage), cageDoorOpenDelay);
        Invoke(nameof(ActivateTiger), tigerActivateDelay);

        if (ambientAudio != null)
            ambientAudio.Play();
    }

    IEnumerator MoveWalls()
    {
        float elapsed = 0f;
        while (elapsed < wallMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = wallEaseCurve.Evaluate(elapsed / wallMoveDuration);
            float d = t * wallMoveDistance;

            if (wallNorth) wallNorth.localPosition = wallNorthStart + Vector3.back  * d;
            if (wallSouth) wallSouth.localPosition = wallSouthStart + Vector3.forward * d;
            if (wallEast)  wallEast.localPosition  = wallEastStart  + Vector3.left  * d;
            if (wallWest)  wallWest.localPosition  = wallWestStart  + Vector3.right * d;

            // Creak sound at 33% and 66% — bool flags ensure exactly one play each
            float ratio = elapsed / wallMoveDuration;
            if (!creak33Played && ratio >= 0.33f) { creak33Played = true; PlaySound(wallCreakClip); }
            if (!creak66Played && ratio >= 0.66f) { creak66Played = true; PlaySound(wallCreakClip); }

            yield return null;
        }
    }

    void OpenCage()
    {
        if (cageDoor == null) return;
        StartCoroutine(RotateCageDoor());
    }

    IEnumerator RotateCageDoor()
    {
        float elapsed = 0f;
        Quaternion startRot = cageDoor.transform.localRotation;
        Quaternion endRot = startRot * Quaternion.Euler(0, cageDoorOpenAngle, 0);

        while (elapsed < cageDoorOpenDuration)
        {
            elapsed += Time.deltaTime;
            cageDoor.transform.localRotation = Quaternion.Lerp(startRot, endRot, elapsed / cageDoorOpenDuration);
            yield return null;
        }
    }

    void ActivateTiger()
    {
        if (tigerAnimator != null)
            tigerAnimator.SetBool("IsActive", true);

        PlaySound(tigerRoarClip);
    }

    void PlaySound(AudioClip clip)
    {
        if (clip == null || ambientAudio == null) return;
        ambientAudio.PlayOneShot(clip);
    }

    public void Deactivate()
    {
        active = false;
        StopAllCoroutines();
        CancelInvoke();
    }
}
