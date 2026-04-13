using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;

namespace Hyper_Casuel_Obstacle
{
    public class HammerAnimation : MonoBehaviour
    {
        [SerializeField] private Transform hammer;
        [SerializeField] private Vector3 rotationVector;
        [SerializeField] private float duraction;


        private void Start()
        {
            StartHammerAnimation();
        }
        private void StartHammerAnimation()
        {
            hammer.DOLocalRotate(rotationVector, duraction, RotateMode.Fast)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.OutQuint);
        }

    }
}