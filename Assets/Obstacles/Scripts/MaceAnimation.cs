using UnityEngine;
using DG.Tweening;
using Unity.Mathematics;


namespace Hyper_Casuel_Obstacle
{
    public class MaceAnimation : MonoBehaviour
    {
        [SerializeField] private Transform mace;
        [SerializeField] private float rotationZ;
        [SerializeField] private float rotateDuration;

        private void Start()
        {
            mace.transform.localRotation = quaternion.Euler(0, 0, -rotationZ);
            StartMaceAnimation();

        }

        private void StartMaceAnimation()
        {
            var rotationVector = new Vector3(0, 0, rotationZ);
            mace.DOLocalRotate(rotationVector, rotateDuration, RotateMode.Fast)
            .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutQuad);
        }
    }
}
