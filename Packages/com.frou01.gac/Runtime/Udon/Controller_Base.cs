
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace frou01.GrabController
{
    public class Controller_Base : UdonSharpBehaviour
    {
        [Header("コントローラーの回転表示オブジェクト")]
        public Transform controllerTransform;
        protected Transform ControllerRoot;
        protected Transform cachedTransform;

        [Header("制御対象のアニメーター")]
        [SerializeField] bool UseAnimator = true;
        public Animator TargetAnimator;
        public Animator[] MultiTargetAnimators;
        [Header("入力先のパラメーター")]
        [Header("(設定名)_position")]
        [Header("(設定名)_normpos")]
        [Header("(設定名)_segment")]
        [Header("を使用します")]
        public string paramaterName;

        [Header("コントローラーの切れ目部分（跨ぐ際に振動が発生します）")]
        [Header("最初と最後は回転角度制限になります　2つは必ず設定してください")]
        public float[] segment_points;
        public float[] snap_points;

        [SerializeField] bool UseEvent = false;
        [Tooltip("set event per every segment")] public string[] SendingEvent;
        [SerializeField] UdonBehaviour[] eventReceivers;
        [SerializeField] bool SendEventBySync = false;

        public bool useHaptic;

        public bool autoDisable;
        public bool ForceAutoDisable;
        public float autoDisableTime;
        float fromActiveTime;

        protected int positionParamaterID;
        protected int normalizedPositionParamaterID;
        protected int segmentsParamaterID;
        bool hasPosition;
        bool hasNormalizedPosition;
        bool hasSegments;


        protected bool onPick;
        [UdonSynced] bool isPicked;
        protected VRC_Pickup pickup;

        protected Vector3 originPos;
        protected Quaternion originRot;
        protected float position_OnPick;
        protected Vector3 localHandPosition_OnPick;
        protected Quaternion localHandRotation_OnPick;
        protected Vector3 localHandPosition;
        protected Quaternion localHandRotation;

        protected VRCPlayerApi localPlayer;


        public int currentSegment;
        public int[] currentSegment_Exposed = new int[1];
        int prevSegment;

        float currentNormalizePosition;
        public float[] currentNormalizePosition_Exposed = new float[1];
        float prevNormalizePosition;

        float SyncedControllerPosition;

        [Header("VRC上でもアニメーターからの変更で回転させることができます")]
        [Header("デバッグ時はアニメーターの(設定名)_rotationを変更することで確認できます")]
        [UdonSynced(UdonSyncMode.Linear)] public float controllerPosition;
        public float[] controllerPosition_Exposed = new float[1];
        float prevControllerPosition;

        private bool isAnimatorControllPosition;

        protected bool netWork_Updating;
        private float SyncInterval = 10;
        private float SinceLastRequest;

        bool isowner;
        bool positionUpdated;
        bool hasSegmentArray;
        protected VRCPlayerApi.TrackingData trackingData;

        [System.NonSerialized] public bool locked = false;
        [System.NonSerialized] public bool lockedSegment = false;
        [System.NonSerialized] public bool lockedSegment_Dec = false;
        [System.NonSerialized] public bool lockedSegment_Inc = false;

        protected virtual void Start()
        {
            cachedTransform = transform;
            localPlayer = Networking.LocalPlayer;
            originPos = transform.localPosition;
            originRot = transform.localRotation;
            pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
            if (ControllerRoot == null) ControllerRoot = controllerTransform.parent;
            SyncInterval = Random.Range(0.1f, 0.2f);
            pickup.InteractionText = InteractionText;
            positionParamaterID = Animator.StringToHash(paramaterName + "_position");
            normalizedPositionParamaterID = Animator.StringToHash(paramaterName + "_normpos");
            segmentsParamaterID = Animator.StringToHash(paramaterName + "_segment");
            if (UseAnimator)
            {
                hasPosition = HasParameter(positionParamaterID, TargetAnimator);
                hasNormalizedPosition = HasParameter(normalizedPositionParamaterID, TargetAnimator);
                hasSegments = HasParameter(segmentsParamaterID, TargetAnimator);
                isAnimatorControllPosition = TargetAnimator.IsParameterControlledByCurve(positionParamaterID);
            }

            hasSegmentArray = segment_points.Length >= 2;
            SetPosition(controllerPosition);
            autoDisable &= ForceAutoDisable || !isAnimatorControllPosition;
            if (autoDisable) disableThis();
            prevSegment = currentSegment;
            prevControllerPosition = controllerPosition;
            prevNormalizePosition = currentNormalizePosition;
            isowner = Networking.IsOwner(gameObject);
            ApplyToTransform();
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
        public override void OnPickup()
        {
            this.enabled = true;
            onPick = true;
            isPicked = true;
        }
        public override void OnDrop()
        {
            isPicked = false;
            transform.localPosition = originPos;
            transform.localRotation = originRot;
            RequestSerialization();
        }

        public virtual void Update()
        {
            cachedTransform.localPosition = originPos;
        }
        public virtual void LateUpdate()
        {
            //if (localPlayer == null) return;

            if (isowner)
            {
                if (SyncedControllerPosition != controllerPosition)
                {
                    netWork_Updating = true;
                }
                if (prevControllerPosition != controllerPosition)
                {
                    netWork_Updating = true;
                }
                if (UseAnimator)
                {
                    if (hasPosition && controllerPosition != TargetAnimator.GetFloat(positionParamaterID))
                    {
                        controllerPosition = TargetAnimator.GetFloat(positionParamaterID);
                    }
                }
                if (controllerPosition_Exposed[0] != prevControllerPosition)
                    controllerPosition = controllerPosition_Exposed[0];
                if (netWork_Updating) SinceLastRequest += Time.deltaTime;
                if (SinceLastRequest > SyncInterval)
                {
                    SinceLastRequest = 0;
                    netWork_Updating = false;
                    RequestSerialization();
                }
            }
            if (isPicked)
            {
                if (isowner && !locked) onPicked();
            }
            else if (!isowner)
            {
                if (UseAnimator)
                {
                    if (hasPosition && controllerPosition != TargetAnimator.GetFloat(positionParamaterID))
                    {
                        controllerPosition = TargetAnimator.GetFloat(positionParamaterID);
                    }
                }
                if (controllerPosition_Exposed[0] != prevControllerPosition)
                    controllerPosition = controllerPosition_Exposed[0];
            }
            positionUpdated = prevControllerPosition != controllerPosition;
            if (positionUpdated)
            {
                CheckSegmentAndUpdate(false);
            }

            DataUpdateCheckAndSend();

            cachedTransform.localPosition = originPos;
            transform.localRotation = originRot;

            if ((!isPicked || !isowner) && autoDisable) fromActiveTime += Time.deltaTime;
            if (autoDisable && !isPicked && fromActiveTime > autoDisableTime) disableThis();
        }

        private void CheckSegmentAndUpdate(bool ignoreLock)
        {
            if (hasSegmentArray)
            {
                float leverPosition_temp = controllerPosition;
                prevNormalizePosition = currentNormalizePosition;

                if (!lockedSegment || ignoreLock)
                {
                    //上探索と下探索を分離して振動=無限ループを回避
                    if (!lockedSegment_Inc || ignoreLock)
                        while (true)
                        {
                            if (segment_points[currentSegment] > leverPosition_temp)
                            {
                                if (currentSegment > 0)
                                {
                                    currentSegment--;
                                }
                                else
                                {
                                    leverPosition_temp = segment_points[currentSegment];
                                    break;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    if (!lockedSegment_Dec || ignoreLock)
                        while (true)
                        {
                            if (segment_points[currentSegment + 1] < leverPosition_temp)
                            {
                                if (currentSegment + 2 < segment_points.Length)
                                {
                                    currentSegment++;
                                }
                                else
                                {
                                    leverPosition_temp = segment_points[currentSegment + 1];
                                    break;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                }

                float nearest = 360;
                float currentDist;
                foreach (float snap_point in snap_points)
                {
                    if (segment_points[currentSegment] < snap_point && snap_point < segment_points[currentSegment + 1])
                    {
                        currentDist = Mathf.Abs(wrapAngleTo180(nearest - leverPosition_temp));
                        if (currentDist < nearest)
                        {
                            leverPosition_temp = snap_point;
                            nearest = currentDist;
                        }
                    }
                }
                currentNormalizePosition = (leverPosition_temp - segment_points[currentSegment]) / (segment_points[currentSegment + 1] - segment_points[currentSegment]);

                controllerPosition = leverPosition_temp;
            }
            controllerPosition_Exposed[0] = controllerPosition;
            currentSegment_Exposed[0] = currentSegment;
            currentNormalizePosition_Exposed[0] = currentNormalizePosition;
        }

        bool AnimatorUpdate;
        private void DataUpdateCheckAndSend()
        {
            if(positionUpdated) ApplyToTransform();
            if (UseAnimator)
            {
                AnimatorUpdate = false;
                if (hasPosition && !isAnimatorControllPosition)
                {
                    TargetAnimator.SetFloat(positionParamaterID, controllerPosition);
                    foreach (Animator Ananimator in MultiTargetAnimators) Ananimator.SetFloat(positionParamaterID, controllerPosition);
                    AnimatorUpdate = true;
                }
                if (currentNormalizePosition != prevNormalizePosition && hasNormalizedPosition)
                {
                    TargetAnimator.SetFloat(normalizedPositionParamaterID, currentNormalizePosition);
                    foreach (Animator Ananimator in MultiTargetAnimators) Ananimator.SetFloat(normalizedPositionParamaterID, currentNormalizePosition);
                    AnimatorUpdate = true;
                }
                if (currentSegment != prevSegment && hasSegments)
                {
                    TargetAnimator.SetInteger(segmentsParamaterID, currentSegment);
                    foreach (Animator Ananimator in MultiTargetAnimators) Ananimator.SetInteger(segmentsParamaterID, currentSegment);
                    AnimatorUpdate = true;
                }
                if (AnimatorUpdate && !TargetAnimator.enabled)
                {
                    TargetAnimator.enabled = true;
                    foreach (Animator Ananimator in MultiTargetAnimators) Ananimator.enabled = true;
                }
            }
            if (currentSegment != prevSegment)
            {
                prevSegment = currentSegment;
                if(UseEvent && (isowner || SendEventBySync) && SendingEvent[currentSegment] != null)foreach (UdonBehaviour reciver in eventReceivers) reciver.SendCustomEvent(SendingEvent[currentSegment]);
            }
            prevControllerPosition = controllerPosition;
            prevNormalizePosition = currentNormalizePosition;
        }

        private void disableThis()
        {
            this.enabled = false;
            fromActiveTime = 0;
        }

        public void SetPosition(float target)
        {
            controllerPosition = target;
            CheckSegmentAndUpdate(true);
            DataUpdateCheckAndSend();

            controllerPosition_Exposed[0] = controllerPosition;
            currentSegment_Exposed[0] = currentSegment;
            currentNormalizePosition_Exposed[0] = currentNormalizePosition;
        }
        protected virtual void onPicked()
        {
        }
        protected virtual void ApplyToTransform()
        {
        }
        protected float wrapAngleTo180(float controllerAngle)
        {
            controllerAngle %= 360;
            controllerAngle = controllerAngle > 180 ? controllerAngle - 360 : controllerAngle;
            controllerAngle = controllerAngle < -180 ? controllerAngle + 360 : controllerAngle;
            return controllerAngle;
        }
        public override void Interact()
        {
        }
        public override void OnPreSerialization()
        {
            SyncedControllerPosition = controllerPosition;
        }
        public override void OnDeserialization()
        {
            //Debug.Log("debug_recieved");
            this.enabled = true;
            CheckSegmentAndUpdate(true);
            DataUpdateCheckAndSend();
        }
        public override void OnOwnershipTransferred(VRC.SDKBase.VRCPlayerApi player)
        {
            isowner = Networking.IsOwner(gameObject);
            this.OnDrop();
        }
#if !COMPILER_UDONSHARP
        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = new Color(1, 1, 1, 0.1f);
            Gizmos.DrawSphere(transform.position, 0.1f * Mathf.Pow(transform.lossyScale.x * transform.lossyScale.y * transform.lossyScale.z, 1 / 3f));
        }
        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1, 1, 1);
            Gizmos.DrawSphere(transform.position, 0.1f * Mathf.Pow(transform.lossyScale.x * transform.lossyScale.y * transform.lossyScale.z, 1 / 3f));
        }
#endif
    }
}
