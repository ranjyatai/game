using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AIScenePickCoordinator
{
    private static readonly Dictionary<AIScenePickKind, IAIScenePickHandler> handlers =
        new Dictionary<AIScenePickKind, IAIScenePickHandler>();

    private static AIScenePickRequest activeRequest;
    private static IAIScenePickHandler activeHandler;
    private static bool sceneGuiHooked;

    public static bool HasActiveRequest => activeRequest != null;
    public static AIScenePickRequest ActiveRequest => activeRequest;

    public static event Action<AIScenePickRequest> OnPickStarted;
    public static event Action<AIScenePickResult, AIScenePickRequest> OnPickCompleted;
    public static event Action<AIScenePickRequest> OnPickCancelled;

    static AIScenePickCoordinator()
    {
        EnsureInitialized();
    }

    public static void EnsureInitialized()
    {
        if (!sceneGuiHooked)
        {
            SceneView.duringSceneGui -= HandleSceneGUI;
            SceneView.duringSceneGui += HandleSceneGUI;
            sceneGuiHooked = true;
        }

        // 确保至少单位 handler 被注册
        AIUnitScenePickHandler.EnsureRegistered();
    }

    public static void RegisterHandler(IAIScenePickHandler handler)
    {
        if (handler == null)
            return;

        handlers[handler.Kind] = handler;
        Debug.Log($"[AIScenePickCoordinator] Registered handler: {handler.Kind}");
    }

    public static bool TryBeginPick(AIScenePickRequest request)
    {
        EnsureInitialized();

        if (request == null)
        {
            Debug.LogWarning("[AIScenePickCoordinator] TryBeginPick failed: request is null.");
            return false;
        }

        if (HasActiveRequest)
        {
            Debug.LogWarning("[AIScenePickCoordinator] TryBeginPick failed: another request is already active.");
            return false;
        }

        if (!handlers.TryGetValue(request.pickKind, out IAIScenePickHandler handler) || handler == null)
        {
            Debug.LogWarning($"[AIScenePickCoordinator] No handler registered for pick kind: {request.pickKind}");
            return false;
        }

        activeRequest = request;
        activeHandler = handler;

        try
        {
            activeHandler.Begin(request);
            AIScenePickFocusGuard.EnterPicking();
            OnPickStarted?.Invoke(request);

            Debug.Log(
                $"[AIScenePickCoordinator] Pick started. " +
                $"kind={request.pickKind}, slotId={request.slotId}, title={request.title}"
            );

            FocusSceneView();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            activeHandler = null;
            activeRequest = null;
            AIScenePickFocusGuard.EndAll();
            return false;
        }
    }

    public static void CancelActivePick()
    {
        if (!HasActiveRequest)
            return;

        AIScenePickRequest cancelledRequest = activeRequest;

        try
        {
            activeHandler?.Cancel();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        finally
        {
            activeHandler = null;
            activeRequest = null;
            AIScenePickFocusGuard.EndAll();
        }

        OnPickCancelled?.Invoke(cancelledRequest);

        Debug.Log($"[AIScenePickCoordinator] Pick cancelled. slotId={cancelledRequest?.slotId}");
    }

    public static void CompleteActivePick(AIScenePickResult result)
    {
        if (!HasActiveRequest)
        {
            Debug.LogWarning("[AIScenePickCoordinator] CompleteActivePick ignored: no active request.");
            return;
        }

        AIScenePickRequest completedRequest = activeRequest;

        activeHandler = null;
        activeRequest = null;
        AIScenePickFocusGuard.EndAll();

        OnPickCompleted?.Invoke(result, completedRequest);

        Debug.Log(
            $"[AIScenePickCoordinator] Pick completed. " +
            $"kind={result?.pickKind}, objectId={result?.objectId}, objectName={result?.objectName}"
        );
    }

    private static void HandleSceneGUI(SceneView sceneView)
    {
        if (!HasActiveRequest || activeHandler == null)
            return;

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            CancelActivePick();
            e.Use();
            return;
        }

        try
        {
            activeHandler.OnSceneGUI(sceneView);

            if (activeHandler.TryGetResult(out AIScenePickResult result) && result != null)
            {
                CompleteActivePick(result);
                e.Use();
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            CancelActivePick();
        }
    }

    private static void FocusSceneView()
    {
        EditorApplication.delayCall += () =>
        {
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Show();
                SceneView.lastActiveSceneView.Focus();
                SceneView.lastActiveSceneView.Repaint();
            }
        };
    }
}
