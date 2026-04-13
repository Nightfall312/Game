using UnityEngine;
using DG.Tweening;
using Unity.Mathematics;

namespace Hyper_Casuel_Obstacle
{
    public class AxeVerticalAnimation : MonoBehaviour
    {
        [SerializeField] private Transform axe;
        [SerializeField] private float rotationZ;
        [SerializeField] private float rotateDuration;

        private void Start()
        {
            axe.transform.localRotation = quaternion.Euler(0, 0, -rotationZ);
            StartAxeAnimation();
        }

        private void StartAxeAnimation()
        {
            var rotationVector = new Vector3(0, 0, rotationZ);
            axe.DOLocalRotate(rotationVector, rotateDuration, RotateMode.Fast)
            .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutQuad);
        }

    }
}

