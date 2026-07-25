using UnityEngine;

// V1 - Shared runtime mask registry.
// The RendererFeature that creates the actual runtime RTs publishes them here.
// Canvas/UI consumers should read from here instead of Resources.FindObjectsOfTypeAll,
// because Resources search can pick stale or preview RenderTextures with the same name.
public static class SkyPrisonRuntimeMaskRegistry_V1
{
    public sealed class ChannelRTs
    {
        public string channelName;
        public RenderTexture characterMask;
        public RenderTexture hiddenMask;
    }

    private static readonly ChannelRTs player = new ChannelRTs { channelName = "Player" };
    private static readonly ChannelRTs enemy = new ChannelRTs { channelName = "Enemy" };
    private static readonly ChannelRTs item = new ChannelRTs { channelName = "Item" };
    private static readonly ChannelRTs ally = new ChannelRTs { channelName = "Ally" };

    public static RenderTexture occluderMaskAll;

    public static void PublishChannel(string channelName, RenderTexture characterMask, RenderTexture hiddenMask)
    {
        ChannelRTs slot = GetSlot(channelName);
        if (slot == null)
            return;

        slot.characterMask = characterMask;
        slot.hiddenMask = hiddenMask;
    }

    public static void PublishOccluderMask(RenderTexture occluderMask)
    {
        occluderMaskAll = occluderMask;
    }

    public static bool TryGetChannel(string channelName, out RenderTexture characterMask, out RenderTexture hiddenMask)
    {
        ChannelRTs slot = GetSlot(channelName);
        if (slot == null)
        {
            characterMask = null;
            hiddenMask = null;
            return false;
        }

        characterMask = slot.characterMask;
        hiddenMask = slot.hiddenMask;
        return characterMask != null && hiddenMask != null;
    }

    public static void Clear()
    {
        player.characterMask = null;
        player.hiddenMask = null;
        enemy.characterMask = null;
        enemy.hiddenMask = null;
        item.characterMask = null;
        item.hiddenMask = null;
        ally.characterMask = null;
        ally.hiddenMask = null;
        occluderMaskAll = null;
    }

    private static ChannelRTs GetSlot(string channelName)
    {
        if (string.IsNullOrEmpty(channelName))
            return null;

        switch (channelName.Trim().ToLowerInvariant())
        {
            case "player":
                return player;
            case "enemy":
                return enemy;
            case "item":
                return item;
            case "ally":
                return ally;
            default:
                return null;
        }
    }
}
