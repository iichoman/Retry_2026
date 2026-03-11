using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class Defalult_Input : MonoBehaviour
{
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool sprint;
    private bool jump;
    
    public bool Attack { get; private set; } = false;
    public bool Interact { get; private set; } = false;
// ===========================================================================
    public Vector2 Move 
    {
        get { return moveInput; } 
        
    }

    public Vector2 Look
    {
        get { return lookInput; }
    }

    public bool Jump { get; private set; } = false;
    public bool Sprint
    {
        get { return sprint; }
    }
// ===========================================================================
// invoke unity events로 전달합니다. 
// ===========================================================================
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>(); // wasd
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        lookInput = context.ReadValue<Vector2>(); // mouse
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        Jump = context.ReadValueAsButton(); // spacebar
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        sprint = context.ReadValueAsButton(); // LShift
    }
    
    public void OnAttack(InputAction.CallbackContext context)
    {
        Attack = context.ReadValueAsButton(); // LClick
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        Interact = context.ReadValueAsButton(); // E
    }

}
