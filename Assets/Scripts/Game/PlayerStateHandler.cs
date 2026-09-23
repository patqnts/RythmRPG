using System.Collections;
using System.Collections.Generic;
using RythmRPG.Combat;
using RythmRPG.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerStateHandler : MonoBehaviour
{
    // Start is called before the first frame update
    public TMP_Text pressMeter;
    public TMP_Text status;
    public KeyButton[] keys;
    private PlayerState playerState;
    public PlayerStateHandler instance;
    private int pressCount;
    private float effectDuration;
    [SerializeField] private LaneInputRouter inputRouter;

    private void Start()
    {
        instance = this;
        playerState = PlayerState.Default;
    }

    private void Update()
    {
        if (playerState == PlayerState.Freeze)
        {
            inputRouter ??= FindAnyObjectByType<LaneInputRouter>();
            if (inputRouter == null) return;
            foreach (LaneKeyBinding binding in inputRouter.Bindings)
            {
                if (GameInput.LanePressed(binding.LaneId))
                {
                    UpdatePressCount(1);
                }

                if (pressCount >= effectDuration)
                {
                    SetPlayerState(PlayerState.Default, 0);
                }
            }
        }
    }

    public void UpdatePressCount(int value)
    {
        pressCount++;
        pressMeter.text = pressCount.ToString();
    }

    public void SetPlayerState(PlayerState state, float duration)
    {
        if(state == PlayerState.None)
        {
            return;
        }

        if(state != playerState)
        {
            playerState = state;
            status.text = playerState.ToString();
            StateEffect(duration);
        }    
    }

    public void StateEffect(float duration)
    {
        switch (playerState)
        {
            case PlayerState.Default:
                BreakFree();
                break;
            case PlayerState.Freeze:
                FreezeKeysForDuration(duration);
                break;
            case PlayerState.Nausea:
                break;
        }  
    }

    public void FreezeKeysForDuration(float duration)
    {
        pressCount = 0;
        effectDuration = duration;
        foreach (KeyButton key in keys)
        {
            key.SetInteractable(false);
            key.isPressed = true;
        } 
    }

    public void BreakFree()
    {
        foreach (KeyButton key in keys)
        {
            key.SetInteractable(true);
            key.isPressed = false;
        }
        SetPlayerState(PlayerState.Default, 0);
    }
}

public enum GameState
{
    Default,
    Combat,
    UI
}
