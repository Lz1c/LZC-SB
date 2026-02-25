//-------------------------------
//--- Prototype FPC
//--- Version 1.0
//--- © The Famous Mouse™
//-------------------------------

using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using PrototypeFPC;

namespace PrototypeFPC
{
    public class Movement : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] Dependencies dependencies;

        [Header("Input Properties")]
        [SerializeField] KeyCode sprintKey = KeyCode.LeftShift;

        [Header("Movement Properties")]
        [SerializeField] float walkSpeed = 6.5f;
        [SerializeField] float sprintSpeed = 12f;
        [SerializeField] float acceleration = 70f;
        [SerializeField] float multiplier = 10f;
        [SerializeField] float airMultiplier = 0.4f;

        [Header("Tilt Properties")]
        [SerializeField] float strafeTilt = 8f;
        [SerializeField] float stafeTiltSpeed = 12f;

        [Header("Drag Properties")]
        [SerializeField] float groundDrag = 6f;
        [SerializeField] float airDrag = 1f;

        [Header("Ground Detection Properties")]
        [SerializeField] Transform groundCheck;
        [SerializeField] float groundCheckRadius = 0.2f;

        [Header("Footstep Properties")]
        [SerializeField] AnimationCurve footstepCurve;
        [SerializeField] float footstepMultiplier = 0.17f;
        [SerializeField] float footstepRate = 0.25f;

        [Header("Audio Properties")]
        [SerializeField] AudioClip[] footstepSound;

        float moveAmount;
        float horizontalMovement;
        float verticalMovement;
        float playerHeight = 2f;
        float curveTime = 0f;
        int randomNum = 0;

        Camera cam;
        Rigidbody rb;
        Transform orientation;
        CapsuleCollider cc;

        Vector3 moveDirection;
        Vector3 slopeMoveDirection;

        RaycastHit slopeHit;
        AudioSource audioSource;

        [HideInInspector]
        [SerializeField]
        List<int>
        playedRandom,
        randomFilter;

        // ===== Added: public API for perks =====
        public void SetWalkSpeed(float value) => walkSpeed = Mathf.Max(0f, value);
        public void SetSprintSpeed(float value) => sprintSpeed = Mathf.Max(0f, value);
        public float GetWalkSpeed() => walkSpeed;
        public float GetSprintSpeed() => sprintSpeed;

        void Start()
        {
            Setup();
        }

        void Update()
        {
            GroundCheck();
            CalculatDirection();
            CalculateSlope();
            ControlSpeed();
            ControlDrag();
            StrafeTilt();
            Footsteps();
        }

        void FixedUpdate()
        {
            Move();
        }

        void Setup()
        {
            transform.gameObject.layer = 2;

            rb = dependencies.rb;
            cc = dependencies.cc;
            cam = dependencies.cam;
            orientation = dependencies.orientation;
            audioSource = dependencies.audioSourceBottom;

            rb.freezeRotation = true;
            rb.mass = 50;
        }

        void GroundCheck()
        {
            dependencies.isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius);
        }

        void CalculatDirection()
        {
            horizontalMovement = Input.GetAxisRaw("Horizontal");
            verticalMovement = Input.GetAxisRaw("Vertical");

            moveDirection = orientation.forward * verticalMovement + orientation.right * horizontalMovement;
        }

        void CalculateSlope()
        {
            slopeMoveDirection = Vector3.ProjectOnPlane(moveDirection, slopeHit.normal);
        }

        void Move()
        {
            if (dependencies.isGrounded && !dependencies.isInspecting && !OnSlope() && !dependencies.isSliding)
            {
                rb.AddForce(moveDirection.normalized * moveAmount * multiplier, ForceMode.Acceleration);
            }

            if (dependencies.isGrounded && OnSlope())
            {
                rb.AddForce(slopeMoveDirection.normalized * moveAmount * multiplier, ForceMode.Acceleration);
            }

            if (!dependencies.isGrounded)
            {
                rb.AddForce(moveDirection.normalized * moveAmount * multiplier * airMultiplier, ForceMode.Acceleration);
            }
        }

        void ControlSpeed()
        {
            if (Input.GetKey(sprintKey) && dependencies.isGrounded)
            {
                moveAmount = Mathf.Lerp(moveAmount, sprintSpeed, acceleration * Time.deltaTime);
            }
            else
            {
                moveAmount = Mathf.Lerp(moveAmount, walkSpeed, acceleration * Time.deltaTime);
            }
        }

        void ControlDrag()
        {
            if (dependencies.isGrounded && !dependencies.isSliding)
            {
                rb.drag = groundDrag;
            }
            else
            {
                rb.drag = airDrag;
            }
        }

        void StrafeTilt()
        {
            if (horizontalMovement != 0f)
            {
                if (horizontalMovement > 0f)
                {
                    var tiltSpeed = stafeTiltSpeed * Time.deltaTime;
                    dependencies.tilt = Mathf.Lerp(dependencies.tilt, -strafeTilt, tiltSpeed);
                }
                else if (horizontalMovement < 0f)
                {
                    var tiltSpeed = stafeTiltSpeed * Time.deltaTime;
                    dependencies.tilt = Mathf.Lerp(dependencies.tilt, strafeTilt, tiltSpeed);
                }
            }
        }

        void Footsteps()
        {
            if (dependencies.isGrounded || dependencies.isWallRunning)
            {
                if (!dependencies.isVaulting && !dependencies.isInspecting && !dependencies.isSliding)
                {
                    Vector2 inputVector = new Vector2(horizontalMovement, verticalMovement);

                    if (inputVector.magnitude > 0f)
                    {
                        if (dependencies.isGrounded)
                        {
                            curveTime += Time.deltaTime * footstepRate * moveAmount;
                        }
                        else if (dependencies.isWallRunning)
                        {
                            curveTime += Time.deltaTime * footstepRate * 2.5f * moveAmount;
                        }

                        if (curveTime >= 1)
                        {
                            curveTime = 0f;

                            if (playedRandom.Count == footstepSound.Length)
                            {
                                playedRandom.Clear();
                            }

                            if (playedRandom.Count != footstepSound.Length)
                            {
                                for (int i = 0; i < footstepSound.Length; i++)
                                {
                                    if (!playedRandom.Contains(i))
                                    {
                                        randomFilter.Add(i);
                                    }
                                }

                                randomNum = Random.Range(randomFilter[0], randomFilter.Count);
                                playedRandom.Add(randomNum);
                                audioSource.PlayOneShot(footstepSound[randomNum]);
                                randomFilter.Clear();
                            }
                        }
                    }
                }

                cam.transform.localPosition = new Vector3(
                    cam.transform.localPosition.x,
                    footstepCurve.Evaluate(curveTime) * footstepMultiplier,
                    cam.transform.localPosition.z
                );
            }
        }

        bool OnSlope()
        {
            if (Physics.Raycast(rb.transform.position, Vector3.down, out slopeHit, playerHeight / 2 + 0.5f))
            {
                if (slopeHit.normal != Vector3.up) return true;
                return false;
            }
            return false;
        }
    }
}