using System;
using System.Reflection;
using UnityEngine;

internal static class MagicBeamNoteTargeting
{
    private const string MagicBeamStaticTypeName = "MagicArsenal.MagicBeamStatic";
    private const string SetPositionAndTargetMethodName = "SetBeamPositionAndTarget";
    private const string SetTargetMethodName = "SetBeamTarget";

    public static void ConfigureBeam(Note note, Transform target)
    {
        if (note == null || target == null) return;

        MonoBehaviour[] behaviours = note.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || !IsMagicBeamStatic(behaviour.GetType())) continue;
            ConfigureBeamBehaviour(behaviour, note.transform.position, target);
        }
    }

    private static bool IsMagicBeamStatic(Type type)
    {
        return type != null && (type.FullName == MagicBeamStaticTypeName || type.Name == "MagicBeamStatic");
    }

    private static void ConfigureBeamBehaviour(MonoBehaviour behaviour, Vector3 startPosition, Transform target)
    {
        Type type = behaviour.GetType();
        MethodInfo setPositionAndTarget = type.GetMethod(SetPositionAndTargetMethodName,
            BindingFlags.Instance | BindingFlags.Public);
        if (setPositionAndTarget != null)
        {
            setPositionAndTarget.Invoke(behaviour, new object[] { startPosition, target });
            return;
        }

        behaviour.transform.position = startPosition;
        MethodInfo setTarget = type.GetMethod(SetTargetMethodName, BindingFlags.Instance | BindingFlags.Public);
        if (setTarget != null)
        {
            setTarget.Invoke(behaviour, new object[] { target });
        }
    }
}
