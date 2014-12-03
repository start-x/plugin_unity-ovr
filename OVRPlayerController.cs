/************************************************************************************

Filename    :   OVRPlayerController.cs
Content     :   Player controller interface. 
				This script drives OVR camera as well as controls the locomotion
				of the player, and handles physical contact in the world.	
Created     :   January 8, 2013
Authors     :   Peter Giokaris

Copyright   :   Copyright 2013 Oculus VR, Inc. All Rights reserved.

Licensed under the Oculus VR SDK License Version 2.0 (the "License"); 
you may not use the Oculus VR SDK except in compliance with the License, 
which is provided at the time of installation or download, or which 
otherwise accompanies this software in either electronic or hard copy form.

You may obtain a copy of the License at

http://www.oculusvr.com/licenses/LICENSE-2.0 

Unless required by applicable law or agreed to in writing, the Oculus VR SDK 
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

************************************************************************************/

using UnityEngine;
using System.Collections.Generic;
using System;
using System.IO;

[RequireComponent(typeof(CharacterController))]

//-------------------------------------------------------------------------------------
// ***** OVRPlayerController
//
// OVRPlayerController implements a basic first person controller for the Rift. It is 
// attached to the OVRPlayerController prefab, which has an OVRCameraController attached
// to it. 
// 
// The controller will interact properly with a Unity scene, provided that the scene has
// collision assigned to it. 
//
// The OVRPlayerController prefab has an empty GameObject attached to it called 
// ForwardDirection. This game object contains the matrix which motor control bases it
// direction on. This game object should also house the body geometry which will be seen
// by the player.
//
public class OVRPlayerController : OVRComponent
{
	protected CharacterController 	Controller 		 = null;
	protected OVRCameraController 	CameraController = null;

	public float Acceleration 	   = 0.1f;
	public float Damping 		   = 0.15f;
	public float BackAndSideDampen = 0.5f;
	public float JumpForce 		   = 0.3f;
	public float RotationAmount    = 1.5f;
	public float GravityModifier   = 0.379f;
		
	private float   MoveScale 	   = 1.0f;
	private Vector3 MoveThrottle   = Vector3.zero;
	private float   FallSpeed 	   = 0.0f;
	
	// Initial direction of controller (passed down into CameraController)
	private Quaternion OrientationOffset = Quaternion.identity;			
	// Rotation amount from inputs (passed down into CameraController)
	private float 	YRotation 	 = 0.0f;
	
	// Transfom used to point player in a given direction; 
	// We should attach objects to this if we want them to rotate 
	// separately from the head (i.e. the body)
	protected Transform DirXform = null;
	protected Transform Bicycle = null;
	
	// We can adjust these to influence speed and rotation of player controller
	private float MoveScaleMultiplier     = 1.0f; 
	private float RotationScaleMultiplier = 1.0f; 
	private bool  AllowMouseRotation      = true;
	private bool  HaltUpdateMovement      = false;
	
	// TEST: Get Y from second sensor
	private float YfromSensor2            = 0.0f;
	
	// * * * * * * * * * * * * *
	
	// Awake
	new public virtual void Awake()
	{
		base.Awake();
		
		// We use Controller to move player around
		Controller = gameObject.GetComponent<CharacterController>();
		
		if(Controller == null)
			Debug.LogWarning("OVRPlayerController: No CharacterController attached.");
					
		// We use OVRCameraController to set rotations to cameras, 
		// and to be influenced by rotation
		OVRCameraController[] CameraControllers;
		CameraControllers = gameObject.GetComponentsInChildren<OVRCameraController>();
		
		if(CameraControllers.Length == 0)
			Debug.LogWarning("OVRPlayerController: No OVRCameraController attached.");
		else if (CameraControllers.Length > 1)
			Debug.LogWarning("OVRPlayerController: More then 1 OVRCameraController attached.");
		else
			CameraController = CameraControllers[0];	
	
		// Instantiate a Transform from the main game object (will be used to 
		// direct the motion of the PlayerController, as well as used to rotate
		// a visible body attached to the controller)
		DirXform = null;
		Transform[] Xforms = gameObject.GetComponentsInChildren<Transform>();
		
		for(int i = 0; i < Xforms.Length; i++)
		{
			if(Xforms[i].name == "ForwardDirection")
			{
				DirXform = Xforms[i];
				break;
			}

			if(Xforms[i].name == "OVRPlayerController") //"Bicycle.003")
			{
				Bicycle = Xforms[i];
			}
		}
		
		if(DirXform == null)
			Debug.LogWarning("OVRPlayerController: ForwardDirection game object not found. Do not use.");
		if(Bicycle == null)
			Debug.LogWarning("OVRPlayerController: Bicycle game object not found. Do not use.");
	}

	// Start
	new public virtual void Start()
	{
		base.Start();
		
		InitializeInputs();	
		SetCameras();
	}
		
	// Update 
	new public virtual void Update()
	{
		base.Update();
		
		// Test: get Y from sensor 2 
		if(OVRDevice.SensorCount == 2)
		{
			Quaternion q = Quaternion.identity;
			OVRDevice.GetPredictedOrientation(1, ref q);
			YfromSensor2 = q.eulerAngles.y;
		}
		
		UpdateMovement();

		Vector3 moveDirection = Vector3.zero;
		
		float motorDamp = (1.0f + (Damping * DeltaTime));
		MoveThrottle.x /= motorDamp;
		MoveThrottle.y = (MoveThrottle.y > 0.0f) ? (MoveThrottle.y / motorDamp) : MoveThrottle.y;
		MoveThrottle.z /= motorDamp;

		moveDirection += MoveThrottle * DeltaTime;
		
		// Gravity
		if (Controller.isGrounded && FallSpeed <= 0)
			FallSpeed = ((Physics.gravity.y * (GravityModifier * 0.002f)));	
		else
			FallSpeed += ((Physics.gravity.y * (GravityModifier * 0.002f)) * DeltaTime);	

		moveDirection.y += FallSpeed * DeltaTime;

		// Offset correction for uneven ground
		float bumpUpOffset = 0.0f;
		
		if (Controller.isGrounded && MoveThrottle.y <= 0.001f)
		{
			bumpUpOffset = Mathf.Max(Controller.stepOffset, 
									 new Vector3(moveDirection.x, 0, moveDirection.z).magnitude); 
			moveDirection -= bumpUpOffset * Vector3.up;
		}			
	 
		Vector3 predictedXZ = Vector3.Scale((Controller.transform.localPosition + moveDirection), 
											 new Vector3(1, 0, 1));	
		
		// Move contoller
		Controller.Move(moveDirection);
		
		Vector3 actualXZ = Vector3.Scale(Controller.transform.localPosition, new Vector3(1, 0, 1));
		
		if (predictedXZ != actualXZ)
			MoveThrottle += (actualXZ - predictedXZ) / DeltaTime; 
		
		// Update rotation using CameraController transform, possibly proving some rules for 
		// sliding the rotation for a more natural movement and body visual
		UpdatePlayerForwardDirTransform();
	}
		
	// UpdateMovement
	//
	// COnsolidate all movement code here
	//
	static float sDeltaRotationOld = 0.0f;
	public virtual void UpdateMovement()
	{
		// Do not apply input if we are showing a level selection display
		if(HaltUpdateMovement == true)
			return;
	
		bool moveForward = true;
		bool moveLeft  	 = false;
		bool moveRight   = false;
		bool moveBack    = false;
				
		MoveScale = read_speed();
			
		//*****
		if (Input.GetKey(KeyCode.W)) moveForward = true;
		if (Input.GetKey(KeyCode.S)) moveBack 	 = true; 
			
		if ( (moveForward && moveLeft) || (moveForward && moveRight) ||
			 (moveBack && moveLeft)    || (moveBack && moveRight) )
			MoveScale = 0.70710678f;
			
		// No positional movement if we are in the air
		if (!Controller.isGrounded)	
			MoveScale = 0.0f;
			
		MoveScale *= DeltaTime;
			
		// Compute this for key movement
		float moveInfluence = Acceleration * 0.1f * MoveScale * MoveScaleMultiplier;
			
		// Run!
		if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
			moveInfluence *= 2.0f;
			
		if(DirXform != null)
		{
			if (moveForward)
				MoveThrottle += DirXform.TransformDirection(Vector3.forward * moveInfluence);
			if (moveBack)
				MoveThrottle += DirXform.TransformDirection(Vector3.back * moveInfluence) * BackAndSideDampen;
			if (moveLeft)
				MoveThrottle += DirXform.TransformDirection(Vector3.left * moveInfluence) * BackAndSideDampen;
			if (moveRight)
				MoveThrottle += DirXform.TransformDirection(Vector3.right * moveInfluence) * BackAndSideDampen;
		}
			
		// Rotate
			
		// compute for key rotation
		float rotateInfluence = DeltaTime * RotationAmount * RotationScaleMultiplier;
			
		// Rotate
		float deltaRotation = 0.0f;
		if(AllowMouseRotation == false)
			deltaRotation = Input.GetAxis("Mouse X") * rotateInfluence * 3.25f;
			
		float filteredDeltaRotation = (sDeltaRotationOld * 0.0f) + (deltaRotation * 1.0f);
		YRotation += filteredDeltaRotation;
		sDeltaRotationOld = filteredDeltaRotation;
			
		// * * * * * * * * * * *
		// XBox controller input	
			
		// Compute this for xinput movement
		moveInfluence = Acceleration * 0.1f * MoveScale * MoveScaleMultiplier;
			
		// Run!
		moveInfluence *= 1.0f + 
					     OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.LeftTrigger);
			
		// Move
		if(DirXform != null)
		{
			float leftAxisY = 
				OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.LeftYAxis);
				
			float leftAxisX = 
			OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.LeftXAxis);
						
			if(leftAxisY > 0.0f)
	    		MoveThrottle += leftAxisY *
				DirXform.TransformDirection(Vector3.forward * moveInfluence);
				
			if(leftAxisY < 0.0f)
	    		MoveThrottle += Mathf.Abs(leftAxisY) *		
				DirXform.TransformDirection(Vector3.back * moveInfluence) * BackAndSideDampen;
				
			if(leftAxisX < 0.0f)
	    		MoveThrottle += Mathf.Abs(leftAxisX) *
				DirXform.TransformDirection(Vector3.left * moveInfluence) * BackAndSideDampen;
				
			if(leftAxisX > 0.0f)
				MoveThrottle += leftAxisX *
				DirXform.TransformDirection(Vector3.right * moveInfluence) * BackAndSideDampen;

		}
			
		float rightAxisX = 
		OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.RightXAxis);
			
		// Rotate
		YRotation += rightAxisX * rotateInfluence;    
		
	// Update cameras direction and rotation
	SetCameras();

	}

	// UpdatePlayerControllerRotation
	// This function will be used to 'slide' PlayerController rotation around based on 
	// CameraController. For now, we are simply copying the CameraController rotation into 
	// PlayerController, so that the PlayerController always faces the direction of the 
	// CameraController. When we add a body, this will change a bit..
	

	public virtual void UpdatePlayerForwardDirTransform()
	{
		float current_position = 0;
		if ((DirXform != null) && (CameraController != null))
		{
					current_position = read_rotation();

					//vetor identidade
					Quaternion i = Quaternion.identity;

					// rotataciona a bicicleta
					Quaternion d = Quaternion.Euler (new Vector3 (0, current_position, 0));
					Bicycle.rotation = i * d;
		
					//Ajustando a CameraControler
					CameraController.SetYRotation (YRotation + current_position);

					//rotaciona o vetor direçao
					d = Quaternion.Euler (new Vector3 (0, current_position, 0));
					DirXform.rotation = i * d ;
		
					/// write file
					write_altitude();
		}
	}

	///////////////////////////////////////////////////////////
	// PUBLIC FUNCTIONS
	///////////////////////////////////////////////////////////
	
	// Jump
	public bool Jump()
	{
		if (!Controller.isGrounded)
			return false;

		MoveThrottle += new Vector3(0, JumpForce, 0);

		return true;
	}

	// Stop
	public void Stop()
	{
		Controller.Move(Vector3.zero);
		MoveThrottle = Vector3.zero;
		FallSpeed = 0.0f;
	}	
	
	// InitializeInputs
	public void InitializeInputs()
	{
		// Get our start direction
		OrientationOffset = transform.rotation;
		// Make sure to set y rotation to 0 degrees
		YRotation = 0.0f;
	}
	
	// SetCameras
	public void SetCameras()
	{
		if(CameraController != null)
		{
			// Make sure to set the initial direction of the camera 
			// to match the game player direction
			CameraController.SetOrientationOffset(OrientationOffset);
			CameraController.SetYRotation(YRotation);
		}
	}
	
	// Get/SetMoveScaleMultiplier
	public void GetMoveScaleMultiplier(ref float moveScaleMultiplier)
	{
		moveScaleMultiplier = MoveScaleMultiplier;
	}
	public void SetMoveScaleMultiplier(float moveScaleMultiplier)
	{
		MoveScaleMultiplier = moveScaleMultiplier;
	}
	
	// Get/SetRotationScaleMultiplier
	public void GetRotationScaleMultiplier(ref float rotationScaleMultiplier)
	{
		rotationScaleMultiplier = RotationScaleMultiplier;
	}
	public void SetRotationScaleMultiplier(float rotationScaleMultiplier)
	{
		RotationScaleMultiplier = rotationScaleMultiplier;
	}
	
	// Get/SetAllowMouseRotation
	public void GetAllowMouseRotation(ref bool allowMouseRotation)
	{
		allowMouseRotation = AllowMouseRotation;
	}
	public void SetAllowMouseRotation(bool allowMouseRotation)
	{
		AllowMouseRotation = allowMouseRotation;
	}
	
	// Get/SetHaltUpdateMovement
	public void GetHaltUpdateMovement(ref bool haltUpdateMovement)
	{
		haltUpdateMovement = HaltUpdateMovement;
	}
	public void SetHaltUpdateMovement(bool haltUpdateMovement)
	{
		HaltUpdateMovement = haltUpdateMovement;
	}
	
	/*
	string file_position = "files/unity_position.txt";
	string file_speed 	 = "files/unity_speed.txt";        
	string file_rotation = "files/unity_rotation.txt";   
	string file_altitude = "files/unity_altitude.txt";
	*/

	string file_position = "C:\\Users\\macartur_\\Desktop\\files\\unity_position.txt";
	string file_speed 	 = "C:\\Users\\macartur_\\Desktop\\files\\unity_speed.txt";        
	string file_rotation = "C:\\Users\\macartur_\\Desktop\\files\\unity_rotation.txt";   
	string file_altitude = "C:\\Users\\macartur_\\Desktop\\files\\unity_altitude.txt";


	//  unity position
	void write_position(){
		Vector3 position = Bicycle.transform.position;
		string position_text = String.Format ("{0} {1} {2}", position.x.ToString (),position.y.ToString (), position.z.ToString ());	                                      
		write_file(position_text ,file_position);
	}
	//unity rotation
	void write_rotation(){
		write_file(Bicycle.transform.rotation.eulerAngles.y.ToString(),file_rotation);
	}

	//unity altitude
	void  write_altitude(){
		write_file (Bicycle.position.y.ToString(), file_altitude);
	}

	float current_speed = 0.0f;
	float read_speed(){
		float next_speed = float.Parse(read_file (file_speed));
		if (next_speed == 0.0f) {
			current_speed = 0.0f;
			return current_speed;
		}
		current_speed =  Math.Abs(next_speed - current_speed);
		return current_speed;
	}

	float current_rotation = 0.0f;
	Boolean can_read = true;
	float read_rotation(){

		if (! can_read) {
			can_read = true;
			return current_rotation;
		}

		float previous_rotation = 0.0f;
		float difference = 0.0f;
		float next_rotation = 0.0f;

		next_rotation = float.Parse(read_file (file_rotation));
		difference = next_rotation - previous_rotation;
		current_rotation += (difference == 0.0f ? previous_rotation : difference);
		previous_rotation = next_rotation;
		can_read = false;
		return current_rotation;

	}

	void OnGUI(){
		// get Control C
		if (Event.current.Equals(Event.KeyboardEvent("^c"))) {
			//System.Diagnostics.Process.Start("notepad","C:\\Users\\macartur_\\Desktop\\teste.txt");
		}
	}

	//method to read a file
	string read_file(string file){
		StreamReader sr = new StreamReader (file);
		string text =  sr.ReadToEnd();
		sr.Close ();
		return text;
	}
			

	
	void write_file(string text, string file){
		StreamWriter sw = new StreamWriter(file);
		sw.WriteLine (text);
		sw.Flush ();
		sw.Close ();
	}
}