using System;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedHouse.Dialogue
{
    [CreateAssetMenu(fileName = "DialogueSequence", menuName = "Haunted House/Dialogue/Dialogue Sequence")]
    public sealed class DialogueSequence : ScriptableObject
    {
        [SerializeField] private bool pauseGame = true;
        [SerializeField, Min(0f)] private float autoAdvanceDelay = 2f;
        [SerializeField] private List<DialogueLine> lines = new();

        public bool PauseGame => pauseGame;
        public float AutoAdvanceDelay => autoAdvanceDelay;
        public IReadOnlyList<DialogueLine> Lines => lines;
    }

    [Serializable]
    public sealed class DialogueLine
    {
        [SerializeField] private DialogueSpeaker speaker = DialogueSpeaker.Radio;

        [TextArea(2, 5)]
        [SerializeField] private string text;

        public DialogueSpeaker Speaker => speaker;
        public string Text => text ?? string.Empty;
    }
}
