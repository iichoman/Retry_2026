using UnityEngine;
public class Player : MonoBehaviour
{
    [SerializeField] private string playerId;
    private Player_State state;
    private Player_Attack attack;

    public string PlayerId => playerId;
    public Player_State State => state;
    public Player_Attack Attack => attack;
    private void Awake()
    {
        state = GetComponent<Player_State>();
    }
}
