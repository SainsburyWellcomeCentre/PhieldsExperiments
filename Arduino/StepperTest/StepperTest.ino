/*
	Test sketch for the Phields Loom drop test
*/

#include "FastStepper.h"

#include "SerialCommand.h"

#include "StateMachine.h"

// Hardware pins connected to the motor controller

// The STEP and DIR pins are mandatory, SLEEP(sleep/enable) pin is optional
// The STEP pin sends the step pulses to the controller
// The DIR pin sends the direction the motor should move to
// The SLEEP pin sends allows the stepper to be disabled when desired
const uint8_t STEP_PIN = 3;
const uint8_t DIR_PIN = 4;
const uint8_t SLEEP_PIN = 5;

// Pin for the button so start/reset the loom drop
const uint8_t DROP_BUTTON_PIN = 6;

// Pin to activate the motor brake
const uint8_t MOTOR_BRAKE_PIN = 7;


// Multiplier value to apply to the microstepping calculations
const uint32_t MOTOR_MICROSTEPPING_COMPENSATION = 4;

// Hardware limitations for the motor
// Minimum motor speed (in steps/second) 31 is the minimum value the arduino supports. DO NOT DECREASE!
const uint16_t MOTOR_MIN_SPEED = 31;
// Maximum motor speed (in steps/second) 32767 is the maximum value supported in this library. DO NOT INCREASE!
const uint16_t MOTOR_MAX_SPEED = 32767;
//#define MOTOR_MAX_SPEED 200

// Acceleration (in steps/second^2) to be used when dropping the loom in "freefall"
//#define MOTOR_DROP_ACCELERATION 5204
const uint32_t MOTOR_DROP_ACCELERATION = 4000;

// Jerk (in steps/second^3) to be used when dropping the loom in "freefall"
const int32_t MOTOR_DROP_ACCELERATION_JERK = -1000;

// Acceleration (in steps/second^2) to be used when stopping the loom drop
//define MOTOR_DROP_DECELERATION 46855
const uint32_t MOTOR_DROP_DECELERATION = 20000;

// Jerk (in steps/second^3) to be used when dropping the loom in "freefall"
const int32_t MOTOR_DROP_DECELERATION_JERK = 2000;

// Acceleration (in steps/second^2) to be used when pulling the Loom back up
const uint32_t MOTOR_RETRACT_ACCELERATION = 10000;
// Maximum motor speed (in steps/second) to pull the Loom back up
const uint32_t MOTOR_MAX_RESET_SPEED = 8000;

// Total of steps for the complete drop
//#define TOTAL_STEPS_TRAVELED 955
const uint32_t TOTAL_STEPS_TRAVELED = 775;

// Number of milliseconds to wait before engaging/disengaging the break
const uint32_t BRAKE_ENGAGE_DELAY = 500;

// Time (ms) to wait after we finish a drop to start retracting the loom
const uint32_t LOOM_BOTTOM_PAUSE = 2000;

// Time (ms) to wait after we finish retracting the loom before starting the next drop
const uint32_t LOOM_DROP_PAUSE = 1000;



// Settings for the motor operation
typedef struct
{
  uint32_t microstepping_compensation;
  uint32_t brake_engage_delay;

} MotorParameters;

MotorParameters _motor_parameters = {MOTOR_MICROSTEPPING_COMPENSATION, BRAKE_ENGAGE_DELAY};


// Setting regarding the loom configuration
typedef struct
{
  uint32_t loom_drop_distance;
  uint32_t loom_bottom_pause;
  uint32_t loom_drop_pause;

} LoomParameters;

LoomParameters _loom_parameters = { TOTAL_STEPS_TRAVELED, LOOM_BOTTOM_PAUSE, LOOM_DROP_PAUSE };


// Data necessary to configure a motor movement type
typedef struct
{
  uint32_t min_speed;
  uint32_t max_speed;
  uint32_t acceleration;
  uint32_t deceleration;
  uint32_t acceleration_jerk;
  uint32_t deceleration_jerk;

} MovementParameters;

// Settings for the movement of dropping the loom
MovementParameters _motor_drop_parameters = { MOTOR_MIN_SPEED, MOTOR_MAX_SPEED, MOTOR_DROP_ACCELERATION, MOTOR_DROP_DECELERATION, MOTOR_DROP_ACCELERATION_JERK, MOTOR_DROP_DECELERATION_JERK };
// Settings for the movement of bringing the loom back up
MovementParameters _motor_reset_parameters = { MOTOR_MIN_SPEED, MOTOR_MAX_RESET_SPEED, MOTOR_RETRACT_ACCELERATION, MOTOR_RETRACT_ACCELERATION, 0, 0 };


// Class to send the control signals to the stepper using the desired acceleration curves
FastStepper _stepper;

// Class to process the incoming serial commands
SerialCommand _command;

// Time when the motor start moving to start the drop
int32_t _drop_start_time;
// Time when the motor stops moving to finish the drop
int32_t _drop_finish_time;


// Number of remaining drops to perform
int32_t _remaining_drops;


// Define all the existing system states for the loom drop
enum SystemState {
  SystemStateIdle,
  SystemStateStartingDrop,
  SystemStateReleasingBrake,
  SystemStateDropping,
  SystemStateBottom,
  SystemStateResetting,
  SystemStateEngagingBrake,
  SystemStateFinishingDrop,
  SystemStateDelayNextDrop
};

// Create a state machine to process the loom drop
StateMachine _loom_machine;


void setup() {
  Serial.begin(1000000);
  Serial.println(F("Stepper Loom Controller Initializing!"));

  pinMode(DROP_BUTTON_PIN, INPUT_PULLUP);
  pinMode(MOTOR_BRAKE_PIN, OUTPUT);

  // Initialize the stepper control pins
  _stepper.begin(STEP_PIN, DIR_PIN, SLEEP_PIN);

  DDRB = DDRB | B00000011;  // Set pin B0 and B1 as an output (for debug only)

  // Add the names and callback functions associated with all the different states
  _loom_machine.addState(SystemStateIdle, "Idle", enterIdle, runIdle, nullptr);
  _loom_machine.addState(SystemStateStartingDrop, "StartingDrop", enterStartingDrop, nullptr, nullptr, SystemStateReleasingBrake, BRAKE_ENGAGE_DELAY);
  _loom_machine.addState(SystemStateReleasingBrake, "ReleasingBrake", enterReleasingBrake, nullptr, nullptr, SystemStateDropping, BRAKE_ENGAGE_DELAY);
  _loom_machine.addState(SystemStateDropping, "Dropping", enterDropping, runDropping, nullptr);
  _loom_machine.addState(SystemStateBottom, "Bottom", nullptr, nullptr, nullptr, SystemStateResetting, LOOM_BOTTOM_PAUSE);
  _loom_machine.addState(SystemStateResetting, "Resetting", enterResetting, runResetting, nullptr);
  _loom_machine.addState(SystemStateEngagingBrake, "EngagingBrake", enterEngagingBrake, nullptr, nullptr, SystemStateIdle, BRAKE_ENGAGE_DELAY);
  //_loom_machine.addState(SystemStateFinishingDrop, "Engaging Brake", enterFinishingDrop, nullptr, nullptr, SystemStateIdle, BRAKE_ENGAGE_DELAY);
  _loom_machine.addState(SystemStateDelayNextDrop, "DelayDrop", nullptr, nullptr, nullptr, SystemStateDropping, LOOM_DROP_PAUSE);

  // Enable the printing of the state transitions to the serial console
  _loom_machine.printStateTransitions(&Serial);

  // Start the machine in the "Idle" state
  _loom_machine.setState(SystemStateIdle);

  // Now setup the incoming serial commands

  setupSerialCommands();
  delay(10);
  Serial.println(F("Setup complete!"));
}

void loop() {
  // Process the incoming serial commands
  _command.readSerial(&Serial);


  // Run the state machine for the loom control
  _loom_machine.run();

  // The run() method needs to be called often in order for the acceleration to happen smoothly.
  // This means we cannot use delay() if the rest of the code, at least when the motor is moving
  _stepper.run();

  //Serial.println(TCNT1); delay(10);
}

// Helper functions to control the brake
inline void releaseBrake() {
  digitalWrite(MOTOR_BRAKE_PIN, false);
}
inline void engageBrake() {
  digitalWrite(MOTOR_BRAKE_PIN, true);
}

// ************************************************************************************
// Callback handlers for the different stages of the state machine that need processing
// ************************************************************************************
void enterIdle() {
  // Make sure the motor in not using power but is stopped
  engageBrake();
  _stepper.sleep();
}

void runIdle() {
  // If we press the button, we start the drop
  if (!digitalRead(DROP_BUTTON_PIN)) _loom_machine.setState(SystemStateStartingDrop);
}

void enterStartingDrop() {
  // Wake the motor and wait for a bit before disengaging the brake
  _stepper.wake();

  // @TODO: Replace this when we install the endstop
  // Make sure we're at 0 position (this will be replaced by the endstop switch)
  _stepper.setPosition(0);
}

void enterReleasingBrake() {
  // Release the brake and wait for a bit before starting the drop
  releaseBrake();
}

void enterDropping() {
  // Set the movement parameters for the drop and start the movement
  _stepper.setMovementSpeed(MOTOR_MIN_SPEED, MOTOR_MAX_SPEED);
  _stepper.setMovementAcceleration(_motor_drop_parameters.acceleration*_motor_parameters.microstepping_compensation, 
                                  _motor_drop_parameters.deceleration*_motor_parameters.microstepping_compensation);
  _stepper.setMovementJerk(_motor_drop_parameters.acceleration_jerk*_motor_parameters.microstepping_compensation, 
                                  _motor_drop_parameters.deceleration_jerk*_motor_parameters.microstepping_compensation);

  _stepper.moveTo(_loom_parameters.loom_drop_distance*_motor_parameters.microstepping_compensation);
  //Serial.print(F("Moving:")); Serial.println(_stepper.getMovementDirection());
  _drop_start_time = millis();
}

void runDropping() {
  // If it's dropping and the movement stops, it means the drop has ended
  if (_stepper.getMovementDirection() == 0) {
    _drop_finish_time = millis();
    _loom_machine.setState(SystemStateBottom);
    Serial.print(F("Drop time (ms):"));
    Serial.println(_drop_finish_time - _drop_start_time);
  }
}

void enterResetting() {
  _stepper.setMovementSpeed(MOTOR_MIN_SPEED, _motor_reset_parameters.max_speed*_motor_parameters.microstepping_compensation);
  _stepper.setMovementAcceleration(_motor_reset_parameters.acceleration*_motor_parameters.microstepping_compensation, 
                                   _motor_reset_parameters.deceleration*_motor_parameters.microstepping_compensation);
  _stepper.setMovementJerk(0, 0);
  _stepper.moveTo(0);
  //Serial.print(F("Moving:")); Serial.println(_stepper.getMovementDirection());
}

void runResetting() {
  // If it's resetting and the movement stops, it means the reset has ended
  if (_stepper.getMovementDirection() == 0) {
    // Now we need to do different things depending if we want to do another drop or not
    _remaining_drops--;

    // If we do have more drops to perform, we wait a bit before the next one
    if (_remaining_drops > 0) _loom_machine.setState(SystemStateDelayNextDrop);
    // If we are done with the drops, let's engage the brake and stop
    else _loom_machine.setState(SystemStateEngagingBrake);
  }
}

void enterEngagingBrake() {
  engageBrake();
}



// Callbacks for the different serial commands

// Completelly releases the motor, allowing it to be freely rotated
void releaseMotor() {
  Serial.println(F("Releasing motor"));
  _stepper.sleep();
  releaseBrake();
}

void engageMotor() {
  Serial.println(F("Engage motor"));
  engageBrake();
}

void zeroPosition() {
  Serial.println(F("Resetting motor position"));
  _stepper.setPosition(0);
}

void startDrops() {
  int32_t drop_count = 0;
  char *arg;

  Serial.println(F("Starting drop sequence"));
  arg = _command.next();
  if (arg != NULL) {
    // Get the desired number of drops as a parameter
    drop_count = atoi(arg);
  }
  // Making sure the number of drops is at least 1
  drop_count = max(1, atoi(arg));

  Serial.print(F("Performing "));
  Serial.print(drop_count);
  Serial.println(F(" drops"));

  // Save the drop count and let's start the first one
  _remaining_drops = drop_count;
  _loom_machine.setState(SystemStateStartingDrop);
}


void moveMotor() 
{
  int32_t parameter;
  char *arg;

  Serial.println(F("Moving the motor"));
  arg = _command.next();
  if (arg != NULL) {
    parameter = atoi(arg);
    _stepper.wake();
    delay(100);
    releaseBrake();
    int32_t current_position = _stepper.getCurrentPosition();
    int32_t target_position = current_position + parameter;
    Serial.print(F("Target: "));
    Serial.println(target_position);

    _stepper.setMovementSpeed(MOTOR_MIN_SPEED, _motor_reset_parameters.max_speed); 
    _stepper.setMovementAcceleration(_motor_reset_parameters.acceleration*_motor_parameters.microstepping_compensation, 
                                  _motor_reset_parameters.deceleration*_motor_parameters.microstepping_compensation);
    _stepper.setMovementJerk(0, 0);
    _stepper.moveTo(target_position);
  } else {
    Serial.println(F("No arguments, can't move"));
  }
}
void setupSerialCommands() 
{
  _command.addCommand("DROP", startDrops);
  _command.addCommand("R", releaseMotor);
  _command.addCommand("E", engageMotor);
  _command.addCommand("M", moveMotor);
  _command.addCommand("Z", zeroPosition);
  _command.addCommand("DropMotorParameters", parseDropMotorParameters);
  _command.addCommand("ResetMotorParameters", parseResetMotorParameters);
  _command.addCommand("LoomParameters", parseLoomParameters);
  _command.addCommand("MotorParameters", parseMotorParameters);

}

void readParameters(int32_t* parameters, int8_t count)
{
  char *arg;
  for (int i = 0; i<count; i++) 
  {
    arg = _command.next();
    if (arg != NULL) 
    {
      parameters[i] = atoi(arg);
    }
    else break;
  }
}

void printParameters(int32_t* parameters, int8_t count)
{
  for (int i = 0; i<count; i++) 
  {
    Serial.print(parameters[i]); Serial.print(' ');    
  }
}

//MovementParameters _motor_drop_parameters = { MOTOR_MIN_SPEED, MOTOR_MAX_SPEED, MOTOR_DROP_ACCELERATION, MOTOR_DROP_DECELERATION};
void parseDropMotorParameters() 
{
  Serial.println(F("parseDropMotorParameters"));
  int32_t parameter[6];

  readParameters(parameter, 6);
  _motor_drop_parameters = {(uint32_t)parameter[0],(uint32_t)parameter[1],(uint32_t)parameter[2],(uint32_t)parameter[3],(uint32_t)parameter[4],(uint32_t)parameter[5]};
  Serial.print("DropMotorParameters: "); printParameters(parameter, 6); Serial.println();
}

//MovementParameters _motor_reset_parameters = {MOTOR_MIN_SPEED, MOTOR_MAX_RESET_SPEED, MOTOR_RETRACT_ACCELERATION, MOTOR_RETRACT_ACCELERATION};
void parseResetMotorParameters() 
{
  Serial.println(F("parseResetMotorParameters"));
  int32_t parameter[4];
  readParameters(parameter, 4);
  _motor_reset_parameters = {(uint32_t)parameter[0],(uint32_t)parameter[1],(uint32_t)parameter[2],(uint32_t)parameter[3]};
  Serial.print("ResetMotorParameters: "); printParameters(parameter, 4); Serial.println();
}

//LoomParameters _loom_parameters = { TOTAL_STEPS_TRAVELED, LOOM_BOTTOM_PAUSE, LOOM_DROP_PAUSE };
void parseLoomParameters() 
{
  Serial.println(F("parseLoomParameters"));
  int32_t parameter[3];

  readParameters(parameter, 3);
  _loom_parameters = {(uint32_t)parameter[0],(uint32_t)parameter[1],(uint32_t)parameter[2]};
  Serial.print("LoomParameters: "); printParameters(parameter, 3); Serial.println();

  _loom_machine.setStateTransition(SystemStateBottom, SystemStateResetting, _loom_parameters.loom_bottom_pause);

}

// MotorParameters _motor_parameters = {MOTOR_MICROSTEPPING_COMPENSATION, BRAKE_ENGAGE_DELAY};
void parseMotorParameters() 
{
  Serial.println(F("parseMotorParameters"));
  int32_t parameter[2];

  readParameters(parameter, 2);
  _motor_parameters = {(uint32_t)parameter[0],(uint32_t)parameter[1]};
  Serial.print("MotorParameters: "); printParameters(parameter, 2); Serial.println();

  _loom_machine.setStateTransition(SystemStateStartingDrop, SystemStateReleasingBrake, _motor_parameters.brake_engage_delay);
  _loom_machine.setStateTransition(SystemStateReleasingBrake, SystemStateDropping, _motor_parameters.brake_engage_delay);
  _loom_machine.setStateTransition(SystemStateEngagingBrake, SystemStateIdle, _motor_parameters.brake_engage_delay);
  //_loom_machine.setStateTransition(SystemStateFinishingDrop, SystemStateIdle, _motor_parameters.brake_engage_delay);
}
