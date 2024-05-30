using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Note : MonoBehaviour
{
    public PlayerStateHandler stateHandler;
    public void SetPlayerState(PlayerState state, float duration)
    {
        stateHandler.SetPlayerState(state, duration);
    }
}
