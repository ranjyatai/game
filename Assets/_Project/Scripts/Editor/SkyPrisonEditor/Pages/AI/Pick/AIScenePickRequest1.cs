using System;
using UnityEngine;

[Serializable]
public class AIScenePickResult
{
    public AIScenePickKind pickKind;

    // 通用标识
    public string objectId;
    public string objectName;
    public string objectType;

    // 场景对象引用（编辑器期可用）
    public UnityEngine.Object sceneObject;

    // 点位类 / 区域类可复用
    public Vector3 worldPosition;
    public Bounds bounds;

    // 扩展字段
    public string extraJson;

    public bool HasValidId()
    {
        return !string.IsNullOrWhiteSpace(objectId);
    }
}
