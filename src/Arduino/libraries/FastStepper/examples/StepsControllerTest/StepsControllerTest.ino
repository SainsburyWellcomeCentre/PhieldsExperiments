/*
    Name:       StepperMotorController.ino
    Created:  07/10/2019 19:35:22
    Author:     Tarquinio Mota
        tarquinio@gmail.com
*/

// Hardware pins connected to the motor controller
// The STEP and DIR pins are mandatory, SLEEP(sleep/enable) pin is optional
#define STEP_PIN         3
#define DIR_PIN          4
#define SLEEP_PIN        5

// Hardware pins connected to movement endstops (Optional)
// When activated, the initial endstop stops the movement instantly and sets the 
// current position to MIN_MOTOR_POSITION
#define INITIAL_ENDSTOP_PIN   8
// When activated, the final endstop just stops the movement instantlly
#define FINAL_ENDSTOP_PIN   9


// Define if we want to process incoming commands from a serial connection
#define ACCEPT_SERIAL_COMMANDS


// Pin to connect an analog input to control the stepper position directlly
//#define STEPPER_TARGET_POSITION_PIN  A0
#if defined(STEPPER_TARGET_POSITION_PIN)
// Value mapped to a reading of 0 on the analog position pin
#define STEPPER_TARGET_MIN_POSITION  500
// Value mapped to a reading of 1023 on the analog position pin
#define STEPPER_TARGET_MAX_POSITION   9000
// Debounce value to read the values from the analog pin. The input value needs 
// to change more than the debounce value to be used
#define STEPPER_TARGET_DEBOUNCE 8
#endif


// Define this value (in ms) to periodically send the motor current position to the serial port
// Values will be sent every x ms, but only while the position is changing
//#define LOG_POSITION_TIMER 20
// Log the values only if the motor position changes
#define LOG_DIFFERENT_VALUES


// Hardware limitations for the motor

// Default Minimum motor speed (in steps/second) 31 is the minimum value the arduino supports. DO NOT DECREASE!
#define MIN_MOTOR_SPEED 31
// Default Maximum motor speed (in steps/second) 32767 is the maximum value supported in this library. DO NOT INCREASE!
#define MAX_MOTOR_SPEED 2000 
// Default acceleration do be used when increasing and decreasing speed
#define MOTOR_ACCELERATION 2000

// Default Minimum position (in steps) the motor is allowed to move into (OPTIONAL)
#define MIN_MOTOR_POSITION  0
// Default Maximum position (in steps) the motor is allowed to move into (OPTIONAL)
#define MAX_MOTOR_POSITION  9600


// Define this value to make the motor move to a random position every X milliseconds
// (using the defined speed and acceleration limits)
//#define TEST_RANDOM_MOVEMNTS 2000
 

// Hardware pins used for other I/O
// Uncomment the defines you need to use the desired funcionalities

#define RELAY_PIN 7



// Add or remove the x after the define to toggle the general debug prints
// This will print the messages:    #define GENERAL_DEBUG(x) x
// This will not print the messages:  #define GENERAL_DEBUG(x)
#define GENERAL_DEBUG(x)
#define SERIAL_DEBUG(x)

#include "Metro.h"
#include "StepsController.h"

StepsController _steps_controller;

#include "MotorControl.h"
#include "SerialControl.h"


void setup()
{
    Serial.begin(115200);
  _steps_controller.begin(STEP_PIN, DIR_PIN, SLEEP_PIN);

  #if defined(MIN_MOTOR_SPEED) && defined(MAX_MOTOR_SPEED) && defined(MOTOR_ACCELERATION)
  _steps_controller.setMovementParameters(MIN_MOTOR_SPEED, MAX_MOTOR_SPEED, MOTOR_ACCELERATION);
  #endif

  #if defined(MIN_MOTOR_POSITION) && defined(MAX_MOTOR_POSITION)
  _steps_controller.setPositionConstraints(MIN_MOTOR_POSITION, MAX_MOTOR_POSITION);
  #endif

  #if defined(INITIAL_ENDSTOP_PIN)
  _steps_controller.setInitialEndstopPin(INITIAL_ENDSTOP_PIN);
  #endif  
  #if defined(FINAL_ENDSTOP_PIN)
  _steps_controller.setFinalEndstopPin(FINAL_ENDSTOP_PIN);
  #endif

  #if defined(STEPPER_TARGET_POSITION_PIN)
  pinMode(STEPPER_TARGET_POSITION_PIN, INPUT);
  #endif

  #if defined(RELAY_PIN)
  digitalWrite(RELAY_PIN, HIGH);
  pinMode(RELAY_PIN, OUTPUT);
  #endif

  GENERAL_DEBUG( Serial.println("Setup complete!"); )
}



void loop()
{
  _steps_controller.run();

  #if defined(ACCEPT_SERIAL_COMMANDS)
  processSerialControl();
  #endif

  #if defined(LOG_POSITION_TIMER)
  reportPositionPeriodically();
  #endif

  #if defined(STEPPER_TARGET_POSITION_PIN)
  processDirectControl();
  #endif

  #if defined(TEST_RANDOM_MOVEMNTS)
  testRandomMovements();
  #endif

  #if defined(RELAY_PIN)
  processRelay();
  #endif
  
  //delay(20);
}
