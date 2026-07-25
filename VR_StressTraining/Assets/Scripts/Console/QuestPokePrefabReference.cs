using UnityEngine;

namespace StressTraining.Console
{
    /// <summary>
    /// Build-safe reference to Meta's package-owned ControllerPokeInteractor.
    /// Keeping the package prefab as the source avoids maintaining a copied fork.
    /// </summary>
    public sealed class QuestPokePrefabReference : ScriptableObject
    {
        [SerializeField] private GameObject controllerPokeInteractor;
        public GameObject ControllerPokeInteractor => controllerPokeInteractor;
    }
}
