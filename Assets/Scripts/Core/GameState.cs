using System;

namespace DinoDigger.Core
{
    /// <summary>Top-level flow state of the game.</summary>
    public enum GameState
    {
        Roam = 0,       // isometric overworld, driving the backhoe
        Transition = 1, // camera easing between roam and dig
        Dig = 2,        // side-view dirt digging mini-game
        Ceremony = 3    // shard-hatch nest ceremony: camera focused on the nest,
                        // dig entry + backhoe move blocked, taps on the new dino still route
    }

    /// <summary>
    /// Plain-C# holder for the current <see cref="GameState"/>. Raises
    /// <see cref="GameEvents.StateChanged"/> whenever it changes.
    /// </summary>
    public class GameStateManager
    {
        public GameState Current { get; private set; } = GameState.Roam;

        public event Action<GameState> Changed;

        public void Set(GameState next)
        {
            if (next == Current)
            {
                return;
            }

            Current = next;
            Changed?.Invoke(next);
            GameEvents.RaiseStateChanged(next);
        }

        public bool Is(GameState s) => Current == s;
    }
}
