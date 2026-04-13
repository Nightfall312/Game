using DG.Tweening;
using UnityEngine;

namespace Hyper_Casuel_Obstacle
{
    public class RotationAnimation : MonoBehaviour
    {
        [SerializeField] private Transform stick;
        [SerializeField] private bool reverse;
        [SerializeField] private float duraction;
        

        private void Start()
        {
            StartAnimation();
        }
        private void StartAnimation()
        {
            if (reverse)
            {
                var rotation = new Vector3(0, -360, 0);
                stick.DOLocalRotate(rotation, duraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
            else
            {
                var rotation = new Vector3(0, 360, 0);
                stick.DOLocalRotate(rotation, duraction, RotateMode.FastBeyond360).SetLoops(-1, LoopType.Restart).SetEase(Ease.Linear).SetRelative(true);
            }
        }
    }
}