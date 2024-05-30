using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStateHandler : MonoBehaviour
{
    // Start is called before the first frame update
    public KeyButton[] keys;
    private PlayerState playerState;
    public PlayerStateHandler instance;

    private void Start()
    {
        instance = this;
        playerState = PlayerState.Default;
    }

    public void SetPlayerState(PlayerState state, float duration)
    {
        if(state != playerState)
        {
            playerState = state;
            StateEffect(duration);
        }    
    }

    public void StateEffect(float duration)
    {
        switch (playerState)
        {
            case PlayerState.Default:
                break;
            case PlayerState.Freeze:
                StartCoroutine(FreezeKeysForDuration(duration));
                break;
            case PlayerState.Nausea:
                break;
            
        }
    }

    IEnumerator FreezeKeysForDuration(float duration)
    {
        foreach (KeyButton key in keys)
        {
            key.SetInteractable(false);
            key.isPressed = true;
        }

        yield return new WaitForSeconds(duration);

        foreach (KeyButton key in keys)
        {
            key.SetInteractable(true);
            key.isPressed = false;
        }
        SetPlayerState(PlayerState.Default, duration);
    }
}

public enum PlayerState
{
    Default,
    Freeze,
    Nausea,
}
