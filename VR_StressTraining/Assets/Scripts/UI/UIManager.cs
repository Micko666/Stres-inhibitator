using System;
using System.Collections.Generic;
using UnityEngine;

namespace StressTraining.UI
{
    /// <summary>
    /// Owns the single world-space menu canvas and panel routing. The state
    /// machine does not drive panels directly (architecture rule) — the
    /// SessionCoordinator asks UIManager to show the panel for each state.
    /// The canvas re-anchors between SafeSpace and corridor UI anchors.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        public Canvas MainCanvas { get; private set; }
        public UiNavigationInput Navigation { get; private set; }

        private readonly Dictionary<Type, IUiPanel> _panels = new Dictionary<Type, IUiPanel>();
        private IUiPanel _activePanel;
        public bool HasActivePanel => _activePanel?.PanelRoot != null && _activePanel.PanelRoot.activeInHierarchy;


        public void Initialize(Transform initialAnchor, Transform viewer, float initialDistanceMeters = 1.6f)
        {
            // SceneZoneController owns the stable spawn-relative anchor pose.
            // Do not overwrite it from centerEyeAnchor here: during the first XR
            // frame that pose may still be at y=0 and would put the panel in the floor.
            _ = viewer;
            _ = initialDistanceMeters;
            MainCanvas = UiBuilder.CreateWorldCanvas("MainUICanvas", initialAnchor, new Vector2(700, 560));
            Navigation = gameObject.AddComponent<UiNavigationInput>();
            // Ray + trigger stays the primary menu path. Thumbstick/A/B is enabled as
            // a fallback so no panel can trap the user, and so the A/B hints the
            // panels print are actually true. Y (pause) is never routed here.
            Navigation.ConfigureControllerNavigation(true);
            Navigation.Nav += OnNav;
        }

        public T RegisterPanel<T>(string panelName) where T : MenuPanelBase
        {
            var panel = gameObject.AddComponent<T>();
            panel.Build(MainCanvas.transform, panelName);
            _panels[typeof(T)] = panel;
            return panel;
        }

        public T GetPanel<T>() where T : class, IUiPanel =>
            _panels.TryGetValue(typeof(T), out var p) ? p as T : null;

        public void ShowPanel(IUiPanel panel)
        {
            foreach (var p in _panels.Values)
                if (!ReferenceEquals(p, panel) && p.PanelRoot != null)
                    p.PanelRoot.SetActive(false);

            _activePanel = panel;
            if (panel?.PanelRoot != null) panel.PanelRoot.SetActive(true);
        }

        public void HideAll()
        {
            foreach (var p in _panels.Values)
                if (p.PanelRoot != null) p.PanelRoot.SetActive(false);
            _activePanel = null;
        }

        /// <summary>Move the menu canvas to a zone anchor (SafeSpace / corridor).</summary>
        public void SetAnchor(Transform anchor, float distanceMeters = 1.6f)
        {
            if (MainCanvas == null || anchor == null) return;
            // The destination anchor is already positioned/oriented from its zone
            // spawn. Reparenting must never move that stable anchor.
            _ = distanceMeters;
            MainCanvas.transform.SetParent(anchor, false);
            MainCanvas.transform.localPosition = Vector3.zero;
            MainCanvas.transform.localRotation = Quaternion.identity;
        }

        private void OnNav(NavEvent nav) => _activePanel?.OnNav(nav);
    }
}
