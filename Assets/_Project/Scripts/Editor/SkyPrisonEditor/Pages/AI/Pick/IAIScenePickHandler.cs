using UnityEditor;

public interface IAIScenePickHandler
{
    AIScenePickKind Kind { get; }

    void Begin(AIScenePickRequest request);
    void Cancel();

    void OnSceneGUI(SceneView sceneView);

    bool TryGetResult(out AIScenePickResult result);
}
