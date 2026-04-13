using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
    public class MovableWall : MonoBehaviour
    {
        [SerializeField] private Transform wall;
        [SerializeField] private float movePosision;
        [SerializeField] private float duration;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            StartAnimation();
        }

        private void StartAnimation()
        {
            wall.DOLocalMoveX(movePosision, duration).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        }
    }
}