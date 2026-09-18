namespace StressTraining.Data
{
    /// <summary>
    /// Semantic input vocabulary shared between the console (input side) and
    /// tasks/UI (consumer side). Tasks never reference physical controls or
    /// GameObjects — only these actions (spec §10, ConsoleBindingDefinition).
    /// </summary>
    public enum SemanticAction
    {
        None = 0,
        Match = 1,
        NoMatch = 2,
        Left = 3,
        Right = 4,
        Go = 5,
        Confirm = 6,
        Cancel = 7,
        SequenceControl1 = 8,
        SequenceControl2 = 9,
        PauseToggle = 10,

        // Nine fixed Corsi buttons (controlIds CORSI_0..CORSI_8). Appended for
        // the Corsi-inspired task — never reorder existing members.
        Corsi0 = 11,
        Corsi1 = 12,
        Corsi2 = 13,
        Corsi3 = 14,
        Corsi4 = 15,
        Corsi5 = 16,
        Corsi6 = 17,
        Corsi7 = 18,
        Corsi8 = 19
    }
}
