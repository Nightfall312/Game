using UnityEngine;
using DG.Tweening;
using Unity.Mathematics;
using UnityEngine.Serialization;


namespace Hyper_Casuel_Obstacle
{
    public class VerticalHammer : MonoBehaviour
    {
        [SerializeField] private Transform hammer; 
        [SerializeField] private float rotationZ;
        [SerializeField] private float rotateDuration;

        private void Start()
        {
            hammer.transform.localRotation = quaternion.Euler(0, 0, -rotationZ);
            StartHammerAnimation();

        }

        private void StartHammerAnimation()
        {
            var rotationVector = new Vector3(0, 0, rotationZ);
            hammer.DOLocalRotate(rotationVector, rotateDuration)
                .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutQuad);
        }
    }
}