
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace frou01.GrabController
{
    public class Controller_Screw : UdonSharpBehaviour
    {
        [Header("レバーに使っているオブジェクトと同じ物にアタッチして使います")]
        [Header("レバーはsegment_pointsの最初を-360、最後を360にする等で回転角度を無制限としてください")]
        [Header("コントローラーの回転表示オブジェクト（通常は変更しません）")]
        public Transform controllerTransform;
        [Header("入出力アニメーター")]
        public Animator TargetAnimator;
        [Header("入力元コントローラー")]
        public Controller_Base BaseController;
        [Header("[Out]出力先のパラメーター")]
        public string ScrewParamaterName;
        [Header("[Out]レバー/バルブの回転量パラメーター")]
        public string LeverParamaterName;
        [Header("スクリューの現在の回転量")]
        [UdonSynced(UdonSyncMode.Linear)] [SerializeField] float screwRotation;
        [Header("デバッグ時はこちらを変更してください")]
        [Header("アニメーターから制御する際はレバーのパラメータを変更するようにしてください")]

        private float[] BaseControllerRotation = new float[1];
        private float[] BaseControllerSegment_points = new float[2];
        public float[] normScrewPosition = new float[1];
        private int ScrewParamaterID;
        private int HandleParamaterID;
        private float prevLeverRotation;
        private float currentLeverRotation;
        [Header("最小回転量　-360より小さい値でも設定可能です")]
        [SerializeField] float min;
        [Header("最大回転量　360より大きい値でも設定可能です")]
        [SerializeField] float MAX;
        private float range = 1;
        private float SyncInterval = 1;
        private float SinceLastRequest;
        private bool dirty;

        bool UseAnimator = true;
        private bool hasHandlePrm;
        private bool isAnimatorControllHandlePrm;
        private bool hasNormalizedPosition;
        void Start()
        {
            range = MAX - min;
            ScrewParamaterID = Animator.StringToHash(ScrewParamaterName);
            HandleParamaterID = Animator.StringToHash(LeverParamaterName);
            UseAnimator = TargetAnimator != null;
            if (UseAnimator)
            {
                hasHandlePrm = HasParameter(HandleParamaterID, TargetAnimator);
                hasNormalizedPosition = HasParameter(ScrewParamaterID, TargetAnimator);
                isAnimatorControllHandlePrm = TargetAnimator.IsParameterControlledByCurve(HandleParamaterID);
                if (hasHandlePrm && currentLeverRotation != TargetAnimator.GetFloat(HandleParamaterID))
                {
                    currentLeverRotation = TargetAnimator.GetFloat(HandleParamaterID);
                }
            }

            BaseControllerRotation = BaseController.controllerPosition_Exposed;
            BaseControllerSegment_points = BaseController.segment_points;

            normScrewPosition[0] = (screwRotation - min) / range;
            wrapedRotation = wrapAngleTo180(screwRotation);
            BaseController.SetPosition(wrapedRotation);

            if (UseAnimator)
            {
                if (hasNormalizedPosition) TargetAnimator.SetFloat(ScrewParamaterID, normScrewPosition[0]);
                if (hasHandlePrm && !isAnimatorControllHandlePrm) TargetAnimator.SetFloat(HandleParamaterID, wrapedRotation);
            }

            if (screwRotation == MAX)
            {
                BaseControllerSegment_points[0] = -360;
                BaseControllerSegment_points[1] = wrapedRotation;
            }
            else if (screwRotation == min)
            {
                BaseControllerSegment_points[0] = wrapedRotation;
                BaseControllerSegment_points[1] = 360;
            }
            else
            {
                BaseControllerSegment_points[0] = -360;
                BaseControllerSegment_points[1] = 360;
            }
            limted = true;

        }
        static bool HasParameter(int paramHash, Animator animator)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.nameHash == paramHash)
                    return true;
            }
            return false;
        }

        private float wrapedRotation;
        bool limted = false;
        public void Update()
        {
            currentLeverRotation = BaseControllerRotation[0];
            if(prevLeverRotation != currentLeverRotation)
            {

                if (Networking.IsOwner(gameObject))
                {
                    if (dirty) SinceLastRequest += Time.deltaTime;
                    if (SinceLastRequest > SyncInterval)
                    {
                        SinceLastRequest = 0;
                        dirty = false;
                        RequestSerialization();
                    }
                    float diff = wrapAngleTo180(currentLeverRotation - prevLeverRotation);
                    if (Mathf.Abs(diff) > 0) dirty = true;
                    //Debug.Log(diff);

                    screwRotation += diff;

                    if (screwRotation < min)
                    {
                        screwRotation = min;
                    }
                    else
                    if (screwRotation > MAX)
                    {
                        screwRotation = MAX;
                    }

                    normScrewPosition[0] = (screwRotation - min) / range;

                    wrapedRotation = wrapAngleTo180(screwRotation);

                    if (UseAnimator)
                    {
                        if (hasNormalizedPosition) TargetAnimator.SetFloat(ScrewParamaterID, normScrewPosition[0]);
                        if (hasHandlePrm && !isAnimatorControllHandlePrm) TargetAnimator.SetFloat(HandleParamaterID, wrapedRotation);
                    }

                    if (currentLeverRotation != wrapedRotation)
                    {
                        BaseControllerRotation[0] = wrapedRotation;
                        BaseController.enabled = true;
                        if (screwRotation == MAX)
                        {
                            BaseControllerSegment_points[0] = -360;
                            BaseControllerSegment_points[1] = wrapedRotation;
                        }
                        else if (screwRotation == min)
                        {
                            BaseControllerSegment_points[0] = wrapedRotation;
                            BaseControllerSegment_points[1] = 360;
                        }
                        else
                        {
                            BaseControllerSegment_points[0] = -360;
                            BaseControllerSegment_points[1] = 360;
                        }
                        limted = true;
                    }
                    else if (limted)
                    {
                        BaseControllerSegment_points[0] = -360;
                        BaseControllerSegment_points[1] = 360;
                    }

                    controllerTransform.localRotation = Quaternion.identity;
                    controllerTransform.Rotate(0, wrapedRotation, 0);
                    prevLeverRotation = wrapedRotation;
                }
            }
        }
        private float wrapAngleTo180(float controllerAngle)
        {
            controllerAngle %= 360;
            controllerAngle = controllerAngle > 180 ? controllerAngle - 360 : controllerAngle;
            controllerAngle = controllerAngle < -180 ? controllerAngle + 360 : controllerAngle;
            return controllerAngle;
        }

        public override void OnDeserialization()
        {
            normScrewPosition[0] = (screwRotation - min) / range;

            wrapedRotation = wrapAngleTo180(screwRotation);

            if (UseAnimator)
            {
                if (hasNormalizedPosition) TargetAnimator.SetFloat(ScrewParamaterID, normScrewPosition[0]);
                if (hasHandlePrm && !isAnimatorControllHandlePrm) TargetAnimator.SetFloat(HandleParamaterID, wrapedRotation);
            }

            limted = true;

            controllerTransform.localRotation = Quaternion.identity;
            controllerTransform.Rotate(0, wrapedRotation, 0);
            prevLeverRotation = wrapedRotation;

        }

        public override void OnPickup()
        {
            if (limted)
            {
                BaseControllerSegment_points[0] = -360;
                BaseControllerSegment_points[1] = 360;
            }
        }
        public override void Interact()
        {
        }
    }
}
