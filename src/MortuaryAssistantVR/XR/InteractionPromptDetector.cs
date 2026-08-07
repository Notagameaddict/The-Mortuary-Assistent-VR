using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace MortuaryAssistantVR.XR;

internal static class InteractionPromptDetector
{
    private static readonly string[] PositiveNameFragments =
    {
        "interact",
        "usable",
        "useable",
        "hover",
        "target",
        "focus",
        "selected",
        "current"
    };

    private static readonly string[] NegativeNameFragments =
    {
        "distance",
        "range",
        "cooldown",
        "timer",
        "input",
        "enabled"
    };

    private static ManualLogSource? _logger;
    private static Component? _playerInteraction;
    private static MemberInfo[]? _candidateMembers;
    private static bool _lastVisible;
    private static bool _initialized;
    private static int _sampleCount;

    internal static void Update(
        ManualLogSource? logger)
    {
        if (logger is not null)
        {
            _logger =
                logger;
        }

        if (!TryResolveComponent())
        {
            SetVisible(
                false);

            return;
        }

        var visible =
            EvaluateCandidates();

        SetVisible(
            visible);
    }

    internal static void Reset()
    {
        _playerInteraction =
            null;

        _candidateMembers =
            null;

        _initialized =
            false;

        _sampleCount =
            0;

        SetVisible(
            false);
    }

    private static bool TryResolveComponent()
    {
        if (_playerInteraction is not null)
        {
            return true;
        }

        var player =
            GameObject.Find(
                "Player");

        if (player is null)
        {
            return false;
        }

        var components =
            player.GetComponents<Component>();

        foreach (var component in components)
        {
            if (component is null)
            {
                continue;
            }

            if (component.GetType().FullName ==
                "PlayerInteraction")
            {
                _playerInteraction =
                    component;

                BuildCandidateMembers(
                    component.GetType());

                _logger?.LogInfo(
                    $"[InteractionPrompt] PlayerInteraction found; " +
                    $"candidateMembers={_candidateMembers?.Length ?? 0}.");

                return true;
            }
        }

        return false;
    }

    private static void BuildCandidateMembers(
        Type type)
    {
        var candidates =
            new List<MemberInfo>();

        var flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        foreach (var field in type.GetFields(flags))
        {
            if (IsCandidate(
                    field.Name,
                    field.FieldType))
            {
                candidates.Add(
                    field);
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (!property.CanRead ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (IsCandidate(
                    property.Name,
                    property.PropertyType))
            {
                candidates.Add(
                    property);
            }
        }

        _candidateMembers =
            candidates.ToArray();

        _initialized =
            true;

        if (_candidateMembers.Length > 0)
        {
            _logger?.LogInfo(
                "[InteractionPrompt] Candidate members: " +
                string.Join(
                    ", ",
                    _candidateMembers.Select(
                        member =>
                            $"{member.Name}:{GetMemberType(member).Name}")));
        }
        else
        {
            _logger?.LogWarning(
                "[InteractionPrompt] No likely interaction-state " +
                "members were found.");
        }
    }

    private static bool IsCandidate(
        string name,
        Type type)
    {
        var lower =
            name.ToLowerInvariant();

        foreach (var negative in NegativeNameFragments)
        {
            if (lower.Contains(
                    negative))
            {
                return false;
            }
        }

        var nameMatches =
            false;

        foreach (var positive in PositiveNameFragments)
        {
            if (lower.Contains(
                    positive))
            {
                nameMatches =
                    true;

                break;
            }
        }

        if (!nameMatches)
        {
            return false;
        }

        return type == typeof(bool) ||
               (!type.IsValueType &&
                type != typeof(string));
    }

    private static Type GetMemberType(
        MemberInfo member)
    {
        return member switch
        {
            FieldInfo field =>
                field.FieldType,

            PropertyInfo property =>
                property.PropertyType,

            _ =>
                typeof(object)
        };
    }

    private static bool EvaluateCandidates()
    {
        if (!_initialized ||
            _playerInteraction is null ||
            _candidateMembers is null)
        {
            return false;
        }

        _sampleCount++;

        foreach (var member in _candidateMembers)
        {
            object? value;

            try
            {
                value =
                    member switch
                    {
                        FieldInfo field =>
                            field.GetValue(
                                _playerInteraction),

                        PropertyInfo property =>
                            property.GetValue(
                                _playerInteraction),

                        _ =>
                            null
                    };
            }
            catch
            {
                continue;
            }

            if (value is bool boolValue)
            {
                if (boolValue)
                {
                    LogPositiveMember(
                        member.Name);

                    return true;
                }

                continue;
            }

            if (value is UnityEngine.Object unityObject)
            {
                if (unityObject is not null)
                {
                    LogPositiveMember(
                        member.Name);

                    return true;
                }

                continue;
            }

            if (value is not null)
            {
                LogPositiveMember(
                    member.Name);

                return true;
            }
        }

        return false;
    }

    private static void LogPositiveMember(
        string memberName)
    {
        if (_sampleCount <= 5 ||
            _sampleCount % 600 == 0)
        {
            _logger?.LogInfo(
                $"[InteractionPrompt] Active member='{memberName}'.");
        }
    }

    private static void SetVisible(
        bool visible)
    {
        if (_lastVisible ==
            visible)
        {
            return;
        }

        _lastVisible =
            visible;

        D3D11PresentHookProbe.SetInteractionPromptVisible(
            _logger,
            visible);

        _logger?.LogInfo(
            $"[InteractionPrompt] VR hand indicator " +
            $"{(visible ? "shown" : "hidden")}.");
    }
}
