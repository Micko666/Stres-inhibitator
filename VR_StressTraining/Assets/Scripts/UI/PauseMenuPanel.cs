using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.UI
{
    public sealed class PauseMenuPanel : MenuPanelBase
    {
        public event Action ContinueRequested;
        public event Action<UserTerminationReason> EndConfirmed;

        private static readonly (string label, UserTerminationReason reason)[] Reasons =
        {
            ("Želim da prekinem", UserTerminationReason.UserRequested),
            ("Nelagodnost", UserTerminationReason.UserDiscomfort),
            ("Simptomi mučnine", UserTerminationReason.SimulatorSickness),
            ("Zadatak nije jasan", UserTerminationReason.TaskUnclear),
            ("Tehnički problem", UserTerminationReason.TechnicalProblem),
            ("Drugo", UserTerminationReason.Other)
        };

        /// <summary>
        /// Pause is opened ONLY by Y on the left controller. This panel is the one
        /// place the two exits live; the tablet just dims the task behind it.
        /// </summary>
        public void ShowMain()
        {
            SetTitle("Pauza");
            SetBody("Sesija je zaustavljena. Globalno vrijeme i pritisak stoje.\n" +
                    "Vrijeme pauze se mjeri odvojeno.");
            SetInfo("Zrak + okidač na dugme · Y (lijevi kontroler) = nastavi");
            SetOptions(new List<(string, Action)>
            {
                ("▶ Nastavi sesiju", () => ContinueRequested?.Invoke()),
                ("■ Završi sesiju", ShowReasons)
            });
        }

        private void ShowReasons()
        {
            SetTitle("Razlog završetka");
            SetBody("Izaberi razlog prekida. Djelimični podaci će biti sačuvani.");
            var options = new List<(string, Action)>();
            foreach (var pair in Reasons)
            {
                var reason = pair.reason;
                options.Add((pair.label, () => EndConfirmed?.Invoke(reason)));
            }
            options.Add(("← Nazad", ShowMain));
            SetOptions(options);
        }
    }
}
