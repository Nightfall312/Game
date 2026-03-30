using UnityEngine;

public static class ConfigurableJointExtensions
{
    public static void SetTargetRotationLocal(
        this ConfigurableJoint joint,
        Quaternion targetLocalRotation,
        Quaternion startLocalRotation
    )
    {
        if (joint.configuredInWorldSpace)
        {
            Debug.LogError(
                "SetTargetRotationLocal should not be used with joints configured in world space. Use SetTargetRotation instead.",
                joint
            );
        }

        SetTargetRotationInternal(joint, targetLocalRotation, startLocalRotation, Space.Self);
    }
 
    public static void SetTargetRotation(
        this ConfigurableJoint cj,
        Quaternion startRot,
        Quaternion target,
        Space space
    )
    {
        Vector3 right = cj.axis;
        Vector3 forward = Vector3.Cross(cj.axis, cj.secondaryAxis).normalized;
        Vector3 up = Vector3.Cross(forward, right).normalized;

        Quaternion localToJointSpace = Quaternion.LookRotation(forward, up);

        if (space == Space.World)
        {
            Quaternion worldToLocal = Quaternion.Inverse(cj.transform.parent.rotation);
            target = worldToLocal * target;
        }

        cj.targetRotation =
            Quaternion.Inverse(localToJointSpace) *
            Quaternion.Inverse(target) *
            startRot *
            localToJointSpace;
    }

    static void SetTargetRotationInternal(
        ConfigurableJoint joint,
        Quaternion targetRotation,
        Quaternion startRotation,
        Space space
    )
    {
        // Build the joint-space rotation basis.
        Vector3 right = joint.axis;
        Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
        Vector3 up = Vector3.Cross(forward, right).normalized;

        Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

        Quaternion resultRotation = Quaternion.Inverse(worldToJointSpace);

        if (space == Space.World)
        {
            resultRotation *= startRotation * Quaternion.Inverse(targetRotation);
        }
        else
        {
            resultRotation *= Quaternion.Inverse(targetRotation) * startRotation;
        }
        resultRotation *= worldToJointSpace;

        joint.targetRotation = resultRotation;
    }
}
