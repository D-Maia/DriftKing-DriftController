using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine.UI;

public class DriftControl : NetworkBehaviour
{

    #region Member Variables

    #region Components References
    private SteeringControl SteerController;
    private SpellCasting SpellCastingController;
    private Rigidbody Rb;

    private Image hudDrift;
    private Toggle devToggle;

    #endregion

    #region Arcadey-feel Applied Forces Variables
    [Header("Arcadey-feel Applied Forces")]
    [SerializeField] private Transform backWheelsForcePoint;
    [SerializeField] private float passiveDriftForce = 1000;
    [SerializeField] private float activeDriftSteerTorque = 3000;
    [SerializeField] private float activeDriftCounterSteerTorque = 25000;
    [SerializeField] private float EndDriftImpulse = 5000;
    [SerializeField] private float throttleDriftForceForward = 2000;
    [SerializeField] private float throttleDriftTorque = 2000;
    [SerializeField] private float throttleDriftTorqueCutoffSpeed = 150;

    #endregion

    #region Pre-drift Variables

    [Header("Pre-drift variables")]
    [SerializeField] private float SuccessfulDriftMaxDot = 0.83f;
    [SerializeField] private float SuccessfulDriftMinDot = - 0.7f; 
    [SerializeField] private float PreDriftRotationSpeed = 10f;

    #endregion

    #region Drift Mechanic Variables
    [Space(15)]
    [Header("Drift Mechanic Variables")]
    [SerializeField] private float MinSpeedToDrift = 50f;
    [SerializeField] private float MinSteerHelperValueWhileDrifting = 0.43f;
    [SerializeField] private float MaxSteerHelperValueWhileDrifting = 0.55f;
    [SerializeField] private float TransferSidewaysMomentumToForwardTimeDuration = 0.8f;
    [HideInInspector] public Drift CurrentDrift;
    private Coroutine SidewaysMomentumTransferCoroutine;
    private bool SidewaysMomentumTransferCoroutineIsRunning;
    private int DriftPercentageInt;

    #endregion

    #region Drift Power Variables

    [Space(15)]
    [Header("Drift Power Variables")]
    [SerializeField] public float MaxDriftPower = 1000f;
    [SerializeField] private float DriftPowerDecayPerSecond = 100f;
    [SerializeField] private float DriftPowerAngleContributionMagnitude;
    [SerializeField] private float DriftPowerDistanceContributionMagnitude;
    [SerializeField] private float DriftPowerDecayDelay = 1f;
    [HideInInspector] public bool DidCastThisDrift;

    [HideInInspector] private bool isDev;
    [HideInInspector] public float CurrentDriftPower = 0f;
    private Coroutine DriftPowerDecayCoroutine;
    private Coroutine DriftPowerDrainCoroutine;
    private bool DriftPowerDecayCoroutineIsRunning;
    private float DriftPowerDecayPerFrame;

    #endregion

    #region State Monitoring

    [HideInInspector] public bool isPressingDriftButton;
    
    public DriftState CurrentDriftState { get; private set; }
    public enum DriftState
    {
        NotDrifting,
        PreDrifting,
        DriftingSuccessfully
    }

    #endregion

    #region Skidmarks Variables

    // SkidMarks
    private WheelCollider[] wheelColliders;
    private Skidmarks[] wheelSkidmarksManagers;
    private int[] lastSkid;
    private const float MAX_SKID_INTENSITY = 50f;
    private const float SKID_FX_SPEED = 5;

    #endregion

    #endregion

    #region Helper Classes & Events Declarations

    public struct Drift
    {
        public bool isValid;
        public float driftDirection;
        public float distanceTravelledDelta;
        public float driftQualityDelta;
        public float forwardToVelocityDot;

        public Drift(bool is_valid)
        {
            isValid = is_valid;
            driftDirection = 0f;
            distanceTravelledDelta = 0f;
            driftQualityDelta = 0f;
            forwardToVelocityDot = 1f;
        }

        public void UpdateDriftValues(Transform carTransform, Rigidbody carRb, float steer, float throttle, float driftPowerAngleContributionMagnitude, float driftPowerDistanceContributionMagnitude)
        {
            forwardToVelocityDot = Vector3.Dot(carTransform.forward, carRb.velocity.normalized);
            driftQualityDelta = CalculateFrameDriftPower(carRb, forwardToVelocityDot, driftPowerAngleContributionMagnitude, driftPowerDistanceContributionMagnitude);
            driftDirection = Mathf.Sign(Vector3.Cross(HelperFunctions.NullifyVectorY(carRb.velocity.normalized), HelperFunctions.NullifyVectorY(carTransform.forward)).y * (1 - Mathf.Abs(forwardToVelocityDot)));
        }

        private float CalculateFrameDriftPower(Rigidbody carRb, float forwardToVelocityDot, float driftPowerAngleContributionMagnitude, float driftPowerDistanceContributionMagnitude)
        {
            if (forwardToVelocityDot < -0.5f)
                return 0f;

            //Got this value from testing. There really is no other way to parametrize distance travelled into values between 0 and 1
            float MaxDistanceTravelledDelta = 13f;

            float DriftAngleDelta = (1 - Mathf.Clamp(forwardToVelocityDot, 0f, 1f));
            float DistanceTravelledDelta = Mathf.Clamp((carRb.velocity.magnitude * Time.fixedDeltaTime), 0f, MaxDistanceTravelledDelta) / MaxDistanceTravelledDelta;

            float driftPowerDelta = (driftPowerAngleContributionMagnitude * DriftAngleDelta) + (DistanceTravelledDelta * driftPowerDistanceContributionMagnitude);
            return driftPowerDelta;
        }
    }

    #endregion

    #region Methods

    #region Initialization

    // Use this for initialization
    void Start()
    {
        hudDrift = ManaDriftManager.Instance.manaFill;

        SteerController = GetComponent<SteeringControl>();
        SpellCastingController = GetComponent<SpellCasting>();
        Rb = GetComponent<Rigidbody>();
        
        wheelColliders = GetComponentsInChildren<WheelCollider>();
        wheelSkidmarksManagers = GetComponentsInChildren<Skidmarks>();
        lastSkid = new int[4];

        //devToggle = GameObject.Find("DevToggle").GetComponent<Toggle>();

        BeginNotDriftingState();
    }

    #endregion

    #region Drift State Methods

    private void BeginNotDriftingState()
    {
        CurrentDriftState = DriftState.NotDrifting;

        CurrentDrift = new Drift(false);
        CurrentDrift.isValid = false;

        ApplyEndDriftForces();

        ChangeFrictionOnTyres(1f);
        SteerController.ToggleSteerHelper(1f, 0f);

        if (!DriftPowerDecayCoroutineIsRunning && DriftPowerDrainCoroutine == null && !DidCastThisDrift)
            DriftPowerDecayCoroutine = StartCoroutine(DecayDriftPower());
    }


    private void BeginPreDriftingState()
    {
        if (CurrentDriftState == DriftState.NotDrifting)
            CurrentDrift = new Drift(true);

        CurrentDriftState = DriftState.PreDrifting;

        ChangeFrictionOnTyres(0f);
        SteerController.ToggleSteerHelper(0f, 0f);

        if (SidewaysMomentumTransferCoroutineIsRunning)
        {
            StopCoroutine(SidewaysMomentumTransferCoroutine);
            SidewaysMomentumTransferCoroutineIsRunning = false;
        }
    }

    private void BeginSuccessfulDriftState()
    {
        CurrentDriftState = DriftState.DriftingSuccessfully;

        ChangeFrictionOnTyres(0.38f);

        if (DriftPowerDecayCoroutineIsRunning)
            StopCoroutine(DriftPowerDecayCoroutine);

        //Rb.angularVelocity = new Vector3(Rb.angularVelocity.x, 1f * Mathf.Sign(Rb.angularVelocity.y), Rb.angularVelocity.z);
    }

    public void IsPressingDriftButton(bool isPressing)
    {
        isPressingDriftButton = isPressing;
    }

    #endregion

    #region Update Methods

    void Update()
    {
        if (isLocalPlayer)
        {
            hudDrift.fillAmount = CurrentDriftPower / MaxDriftPower;
            DriftPercentageInt = (int)( (CurrentDriftPower / MaxDriftPower) * 100 );
            GameObject.Find("Mana_TextAmount").GetComponent<Text>().text = Mathf.Clamp(DriftPercentageInt, 0, 100).ToString();
            DriftMeterGlow();
        }
        //DeveloperMode();
    }

    void DeveloperMode()
    {
        if (devToggle.isOn)
        {
            CurrentDriftPower = MaxDriftPower;
        }
    }

    public void UpdateDriftControls(float steer, float throttle)
    {
        if (!isPressingDriftButton || !SteerController.m_IsGrounded || throttle == 0f)
        {
            if (CurrentDriftState != DriftState.NotDrifting)
                BeginNotDriftingState();
        }

        if (isPressingDriftButton && SteerController.m_IsGrounded && SteerController.CurrentSpeed > MinSpeedToDrift &&  throttle != 0f)
        {
            CurrentDrift.UpdateDriftValues(transform, Rb, steer, throttle, DriftPowerAngleContributionMagnitude, DriftPowerDistanceContributionMagnitude);

            float forwardToVelocityDot = Vector3.Dot(Rb.velocity.normalized, transform.forward);

            bool isPreDrifting = steer != 0f && 
                                 SteerController.CurrentSpeed > MinSpeedToDrift &&
                                 forwardToVelocityDot > SuccessfulDriftMaxDot;

            bool isDriftingSuccessfully = forwardToVelocityDot < SuccessfulDriftMaxDot &&
                                          forwardToVelocityDot > SuccessfulDriftMinDot ||
                                          (forwardToVelocityDot < SuccessfulDriftMinDot && SteerController.CurrentSpeed > MinSpeedToDrift && steer != 0f);

            if (isPreDrifting && !isDriftingSuccessfully && CurrentDriftState != DriftState.PreDrifting)
                BeginPreDriftingState();

            if (isDriftingSuccessfully && CurrentDriftState != DriftState.DriftingSuccessfully)
                BeginSuccessfulDriftState();
        }

        UpdateDriftState(steer, throttle);
    }

    private IEnumerator TransferSidewaysMomentum()
    {
        SidewaysMomentumTransferCoroutineIsRunning = true;
        int numberOfFixedUpdatesForTotalTransfer = Mathf.FloorToInt(TransferSidewaysMomentumToForwardTimeDuration / Time.fixedDeltaTime);
        int numFramesElapsed = 0;

        Vector3 prevFrameLocalVelocity = transform.InverseTransformVector(Rb.velocity);

        float sidewaysVelocity = prevFrameLocalVelocity.x;
        float forwardVelocity = prevFrameLocalVelocity.z;

        float stolenVel = Mathf.Abs(sidewaysVelocity) / (float)numberOfFixedUpdatesForTotalTransfer;

        while (Vector3.Dot(Rb.velocity.normalized, transform.forward) < 0.98f && numFramesElapsed < numberOfFixedUpdatesForTotalTransfer && SteerController.m_IsGrounded)
        {
            Vector3 localVelocity = transform.InverseTransformVector(Rb.velocity);

            if (Vector3.Distance(localVelocity, prevFrameLocalVelocity) > 5f)
                break;

            else
            {
                localVelocity.x = localVelocity.x >= 0 ? localVelocity.x - stolenVel : localVelocity.x + stolenVel;
                localVelocity.z = localVelocity.z >= 0 ? localVelocity.z + (0.6f * stolenVel) : localVelocity.z - (0.6f * stolenVel);

                Rb.velocity = transform.TransformVector(localVelocity);
                prevFrameLocalVelocity = localVelocity;
            }

            numFramesElapsed++;
            yield return new WaitForFixedUpdate();
        }

        SidewaysMomentumTransferCoroutineIsRunning = false;
    }

    private void UpdateDriftState(float steer, float throttle)
    {
        CheckForSkidmarks();

        if (CurrentDriftState != DriftState.NotDrifting)
        {

            if (CurrentDriftState == DriftState.PreDrifting)
                UpdatePreDrift(steer, throttle);

            if (CurrentDriftState == DriftState.DriftingSuccessfully)
                UpdateSuccessfulDrift(steer, throttle);
        }

        if (CurrentDriftState != DriftState.DriftingSuccessfully)
        {
            if (!DriftPowerDecayCoroutineIsRunning && DriftPowerDrainCoroutine == null && !DidCastThisDrift)
                DriftPowerDecayCoroutine = StartCoroutine(DecayDriftPower());
        }
    }

    private void UpdatePreDrift(float steer, float throttle)
    {
        if (!DriftPowerDecayCoroutineIsRunning && DriftPowerDrainCoroutine == null && !DidCastThisDrift)
            DriftPowerDecayCoroutine = StartCoroutine(DecayDriftPower());

        ApplyPreDriftForces(steer, throttle);
    }

    private void UpdateSuccessfulDrift(float steer, float throttle)
    {
        ApplySuccessfulDriftForces(steer, throttle);

        if (!DidCastThisDrift)
            BuildDriftPower();
    }

    #endregion

    #region Drift Power

    public float GetNormalizedDriftPower()
    {
        float power = Mathf.Clamp(CurrentDriftPower, 0f, MaxDriftPower) / MaxDriftPower;
        return power;
    }

    public float GetNormalizedDriftPower(float powerToNormalize)
    {
        float power = Mathf.Clamp(powerToNormalize, 0f, MaxDriftPower) / MaxDriftPower;
        return power;
    }

    private void BuildDriftPower()
    {
        if (DriftPowerDrainCoroutine == null)
        {
            CurrentDriftPower += 0.05f * CurrentDrift.driftQualityDelta;
            CurrentDriftPower = Mathf.Clamp(CurrentDriftPower, 0f, MaxDriftPower);
            if (DriftPowerDecayCoroutineIsRunning)
            {
                StopCoroutine(DriftPowerDecayCoroutine);
                DriftPowerDecayCoroutineIsRunning = false;
            }
        }
    }

    public void GainDriftPower(float amount)
    {
        CurrentDriftPower += amount;
        CurrentDriftPower = Mathf.Clamp(CurrentDriftPower, 0f, MaxDriftPower);
    }

    private IEnumerator DecayDriftPower()
    {
        if (!DriftPowerDecayCoroutineIsRunning)
        {
            DriftPowerDecayCoroutineIsRunning = true;
            float delayToDecay = CurrentDriftPower == MaxDriftPower ? DriftPowerDecayDelay * 3.5f : DriftPowerDecayDelay;

            yield return new WaitForSeconds(delayToDecay);

            while (CurrentDriftState != DriftState.DriftingSuccessfully)
            {
                if (DidCastThisDrift)
                {
                    DriftPowerDecayCoroutineIsRunning = false;
                    StopCoroutine(DriftPowerDecayCoroutine);
                }

                CurrentDriftPower -= DriftPowerDecayPerSecond * Time.deltaTime;
                CurrentDriftPower = Mathf.Clamp(CurrentDriftPower, 0f, MaxDriftPower);
                yield return null;
            }

            DriftPowerDecayCoroutineIsRunning = false;
        }
    }


    public void DepleteDriftPower(float amount, float overDuration, bool depleteMeterFully = false)
    {
        if (DriftPowerDrainCoroutine == null)
            DriftPowerDrainCoroutine = StartCoroutine(DrainDriftPower(amount, overDuration));
    }

    public void DepleteDriftPower(float overDuration, bool depleteMeterFully = false)
    {
        if (DriftPowerDrainCoroutine == null)
            DriftPowerDrainCoroutine = StartCoroutine(DrainDriftPower(CurrentDriftPower, overDuration));
    }

    public void DepleteDriftPowerInstantly(float amount, bool depleteMeterFully = false)
    {
        CurrentDriftPower -= amount;
    }

    private IEnumerator DrainDriftPower(float totalAmount, float duration, bool depleteMeterFully = false)
    {
        DidCastThisDrift = true;
        float timer = 0f;
        float originalDriftPower = CurrentDriftPower;
        float drainedAmountPerSecond = (totalAmount / duration);

        if (DriftPowerDecayCoroutineIsRunning == true)
            StopCoroutine(DriftPowerDecayCoroutine);

        while (timer <= duration)
        {
            CurrentDriftPower -= drainedAmountPerSecond * Time.deltaTime;
            timer += Time.deltaTime;
            yield return null;
        }

        if (depleteMeterFully)
            CurrentDriftPower = 0f;

        DriftPowerDrainCoroutine = null;
        DidCastThisDrift = false;
    }

    #endregion

    #region Drift Forces

    private void ApplyPreDriftForces(float steerInput, float throttleInput)
    {
        if (throttleInput == 0f || steerInput == 0f || SteerController.CurrentSpeed <= 10 || Mathf.Abs(Rb.angularVelocity.y) > PreDriftRotationSpeed)
            return;

        float rawAngularSpeed = PreDriftRotationSpeed;
        float dotProductFactor = Mathf.Max(1f - (1f - CurrentDrift.forwardToVelocityDot) / (1f - SuccessfulDriftMaxDot), 0.5f);
        float speedFactor = Mathf.Clamp(HelperFunctions.CurveFactor(SteerController.CurrentSpeed / SteerController.OriginalTopspeed), 0f, 1f);
        float angularSpeed = rawAngularSpeed * dotProductFactor * speedFactor;

        if (Mathf.Sign(CurrentDrift.driftDirection) == -Mathf.Sign(steerInput))
            angularSpeed /= 1.3f;

        Rb.angularVelocity = new Vector3(Rb.angularVelocity.x, steerInput * angularSpeed, Rb.angularVelocity.z);

        ApplySteerTorque(steerInput * 0.3f);
    }

    public void ApplySuccessfulDriftForces(float steer, float throttle)
    {
        ApplySteerTorque(steer);
        ApplyThrottleTorque(throttle);
        ApplyPassiveDriftForce();
        ApplyThrottleMomentum(throttle);

        float parametrizedDot = SuccessfulDriftMaxDot - CurrentDrift.forwardToVelocityDot;
        float steerHelperValue = MinSteerHelperValueWhileDrifting + parametrizedDot * (MaxSteerHelperValueWhileDrifting - MinSteerHelperValueWhileDrifting);

        SteerController.ToggleSteerHelper(steerHelperValue, 0f);
    }

    private void ApplyThrottleTorque(float throttle)
    {
        if (SteerController.CurrentSpeed < throttleDriftTorqueCutoffSpeed)
        {
            Vector3 throttleTorque = throttle * Vector3.up * CurrentDrift.driftDirection * throttleDriftTorque * Mathf.Max(1, (1 - (SteerController.CurrentSpeed / throttleDriftTorqueCutoffSpeed)));
            Rb.AddTorque(throttleTorque);
        }
    }

    private void ApplySteerTorque(float steer)
    {
        Vector3 steerTorque = Vector3.zero;

        if (CurrentDrift.forwardToVelocityDot > SuccessfulDriftMinDot)
        {
            if (Mathf.Sign(steer) != Mathf.Sign(CurrentDrift.driftDirection))
                steerTorque = steer * transform.up * activeDriftCounterSteerTorque;

            else
                steerTorque = steer * transform.up * activeDriftSteerTorque;
        }
        
        else
        {
            steerTorque = -Mathf.Sign(CurrentDrift.driftDirection) * transform.up * activeDriftCounterSteerTorque * (SuccessfulDriftMinDot - Mathf.Abs(CurrentDrift.forwardToVelocityDot));
        }
        
        Rb.AddTorque(steerTorque);
    }

    private void ApplyPassiveDriftForce()
    {
        Vector3 passiveForce = passiveDriftForce * Vector3.Normalize(transform.forward);
        Rb.AddForce(passiveForce);
    }

    private void ApplyThrottleMomentum(float throttle)
    {
        Vector3 driftThrottle = throttle * transform.forward * throttleDriftForceForward;
        Rb.AddForceAtPosition(driftThrottle, backWheelsForcePoint.position);
    }

    private void ApplyEndDriftForces()
    {
        SidewaysMomentumTransferCoroutine = StartCoroutine(TransferSidewaysMomentum());
    }

    #endregion

    #region Tire Friction Methods

    private void ChangeFrictionOnTyres(float newFrictionValue)
    {
        foreach (WheelCollider wheel in GetComponentsInChildren<WheelCollider>())
        {
            WheelFrictionCurve sidewaysFriction = wheel.sidewaysFriction;

            if (sidewaysFriction.stiffness == newFrictionValue)
                return;

            sidewaysFriction.stiffness = newFrictionValue;

            WheelFrictionCurve forwardFriction = wheel.forwardFriction;
            forwardFriction.stiffness = newFrictionValue;

            wheel.sidewaysFriction = sidewaysFriction;
            wheel.forwardFriction = forwardFriction;
        }
    }

    #endregion

    #region Visual Feedback

    private void CheckForSkidmarks()
    {
        for (int i = 0; i < 4; ++i)
        {
            WheelHit wheelHitInfo;
            WheelCollider wheelCollider = wheelColliders[i];

            if (wheelCollider.GetGroundHit(out wheelHitInfo))
            {
                // Check sideways speed
                // Gives velocity with +Z being our forward axis
                Vector3 localVelocity = transform.InverseTransformDirection(Rb.velocity);
                float skidSpeed = Mathf.Abs(localVelocity.x);
                if (skidSpeed >= SKID_FX_SPEED && SteerController.m_IsGrounded && CurrentDriftState != DriftState.NotDrifting)
                {
                    // MAX_SKID_INTENSITY as a constant, m/s where skids are at full intensity
                    float intensity = Mathf.Clamp01(skidSpeed / MAX_SKID_INTENSITY);
                    Vector3 skidPoint = wheelHitInfo.point + (Rb.velocity * Time.fixedDeltaTime);
                    lastSkid[i] = wheelSkidmarksManagers[i].AddSkidMark(skidPoint, wheelHitInfo.normal, intensity, lastSkid[i]);
                }
                else
                    lastSkid[i] = -1;
            }
            else
                lastSkid[i] = -1;
        }
    }

    public void DriftMeterGlow()
    {
        ManaDriftManager.Instance.energyObj.GetComponent<ParticleSystem>().startSize = CurrentDriftPower;
        float glowAmount = ( HelperFunctions.NormalizeValue(CurrentDriftPower, 0, MaxDriftPower)  );
        ManaDriftManager.Instance.glowImage.color = new Color(1,1,1, glowAmount);

        if (CurrentDriftPower == MaxDriftPower)
            PlayPing();
    }

    private void PlayPing()
    {
        // If I have time I will add a "PING" glow effect once player is at full Drift Power.
    }


    #endregion

    #endregion
}
