using UnityEngine;

public abstract class UnitSpawnCondition : MonoBehaviour
{
    [TextArea(1, 3)]
    [SerializeField] private string note;

    public string Note => note;

    public abstract bool CanSpawn(UnitSpawner spawner, UnitDefinition definition);
}