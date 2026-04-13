using System;
using UnityEngine;
using DG.Tweening;

namespace Hyper_Casuel_Obstacle
{
   public class LongWall : MonoBehaviour
   {
      [SerializeField] private Transform wall;
      [SerializeField] float openDuration;
      [SerializeField] float closeDuration;
      [SerializeField] float openWaitDuration;
      [SerializeField] float closeWaitDuration;


      private void Start()
      {
         StartAnimation();
      }

      private void StartAnimation()
      {
         Vector3 openTransform = new Vector3(0, -90, 0);
         Vector3 closeTransform = new Vector3(0, 0, 0);
         Tween open = wall.DOLocalRotate(openTransform, openDuration).SetEase(Ease.OutBack);
         Tween close = wall.DOLocalRotate(closeTransform, closeDuration).SetEase(Ease.InCirc);

         Sequence wallSequence = DOTween.Sequence();

         wallSequence.Append(open).AppendInterval(openWaitDuration)
            .Append(close).AppendInterval(closeWaitDuration)
            .SetLoops(-1, LoopType.Restart);
         ;
      }
   }
}