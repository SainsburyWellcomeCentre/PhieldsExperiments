/*
							  FastStepper Library

	Author: Tarquinio Mota
	Mail: tarquinio@gmail.com
	Version 1.0.0


	This library can be used to control stepper motors using pulses generated via interrupt timers,
	meaning it can generate fast stepping speeds while running other code in parallel.

	The library also processes accelerations curves, meaning we can define a minimum and maximum 
	velocity and the desired acceleration. Then we just give it the target position and the 
	library handles the rest. The supported velocities are between 31 and 32767 steps per second.

	It also handles endstops, making sure the stepper never moves outside the desired positions.

	We can control multiple steppers by creating multiple instances of the library. 
	The maximum number of steppers that can be controlled depend on the available timers on the arduino.
	ATmega328 has 3 can control 3 steppers, and the ATmega2560 can control 6 steppers)

	Note that using all the timers may cause other libraries to stop working, and even arduino 
	functions like analogWrite or millis()

	The low-level parts of the library (the code to generate the pulses) are based in the arduino
	Tone library, by Brett Hagman

*/

#ifndef _FAST_STEPPER_H_
#define _FAST_STEPPER_H_

#include <stdint.h>

// Default Minimum motor speed (in steps/second) 31 is the minimum value the arduino supports. DO NOT DECREASE!
#define DEFAULT_MIN_MOTOR_SPEED 31
// Default Maximum motor speed (in steps/second) 32767 is the maximum value supported in this library. DO NOT INCREASE!
#define DEFAULT_MAX_MOTOR_SPEED 32767 
// Default acceleration do be used when increasing and decreasing speed
#define DEFAULT_MOTOR_ACCELERATION 500


// Print debug events to the serial console
// Remove the x in the end of each line to stop all the prints
#define DB_EVENTS(x) x
#define DB_ERRORS(x) x


class FastStepper
{
 public:
	// Initialize the stepper controller 
    void begin(int8_t step_pin, int8_t direction_pin, int8_t sleep_pin = -1);

	// Run the controller. This needs to be called often in the main loop in 
	void run();

	// Initialize the speed settings
	void setMovementSpeed(uint16_t min_speed, uint16_t max_speed);

	// Initialize the acceleration settings
	void setMovementAcceleration(uint32_t acceleration, uint32_t deceleration);

	// Initialize the jerk settings
	void setMovementJerk(int32_t acceleration_jerk, int32_t deceleration_jerk);

	// Set the movement limits
	void setPositionConstraints(int32_t min_position, int32_t max_position);

	// Set the pins for the initial and final endstops
	void setStartEndstopPin(int8_t pin);
	void setFinishEndstopPin(int8_t pin);

	// Home the motor to the initial position
	void homeStart(uint16_t velocity = 0) { home(velocity, -1); };
	// Home the motor to the final position
	void homeEnd(uint16_t velocity = 0) { home(velocity, +1); };

	// Move to a target position. If a specific velocity is specified, the movement
	// happens at than constant speed.
	// If a velocity is not specified, the movement happens using the minimum and 
	// maximum velocity and acceleration parameters
	void moveTo(int32_t target, uint16_t velocity = 0);

	// Set the current position of the stepper (sets the value without moving)
	void setPosition(int32_t position);

	// Get the direction of the movement
	// -1 if it's going backward
	// 0 is it's stopped
	// +1 if it's going forward
	int8_t getMovementDirection();

	// Get the target position the stepper is moving into
	int32_t getTargetPosition();

	// Get the current position of the stepper
	int32_t getCurrentPosition();

	// Get the current velocity of the stepper
	int32_t getVelocity();

	// Stop the movement of the stepper immediatelly
    void stop();

	// Set the stepper controller in sleep mode. It keeps track of the position
	// but it will not put any power into the motor
	void sleep();

	// Wakes the controller from the sleep() mode, powering the motor back up
	void wake();

	// Specify if we should invert the movement direction of the motor
	void invertMovement(bool invert);


private:

	void home(uint16_t velocity, int8_t direction);


	void updateVelocity();

	// Calculate how many steps it takes to decelerate to the minimum velocity from the given velocity
	uint32_t decelerationDistance(uint32_t current_speed);

	// Number of pins used by the multiple instances of this library
    static uint8_t _steps_pin_count;

	// Pin used for the steps
	int8_t _step_pin = -1;
	// Pin used for the direction of the steps
	int8_t _direction_pin = -1;
	// Pin used to make the controller sleep (optional)
	int8_t _sleep_pin = -1;

	// Pin used for the initial endstop (optional)
	int8_t _initial_endstop_pin = -1;
	// Pin used for the final endstop (optional)
	int8_t _final_endstop_pin = -1;

	// Are we currently homing the system (looking for the endstops)?
	bool _homing;

	// Timer used to set the frequency of the movement
    int8_t _timer;

	// Port and bitmask for the pin used to control the direction of the movement
	volatile uint8_t *_direction_pin_port;
	volatile uint8_t _direction_pin_mask;


	// Minimum motor speed (in steps/second) 31 is the minimum value the arduino supports. Smaller values will be set to 31.
	uint32_t _min_velocity = DEFAULT_MIN_MOTOR_SPEED;
	// Maximum motor speed (in steps/second)
	uint32_t _max_velocity = DEFAULT_MAX_MOTOR_SPEED;
	// Default acceleration do be used when increasing speed
	uint32_t _acceleration = DEFAULT_MOTOR_ACCELERATION;
	// Default deceleration do be used when decreasing speed
	uint32_t _deceleration = DEFAULT_MOTOR_ACCELERATION;
	// Default acceleration jerk do be used when increasing speed
	int32_t _acceleration_jerk = 0;
	// Default deceleration jerk do be used when decreasing speed
	int32_t _deceleration_jerk = 0;


	// Should we constrain the movement to a min and max positions?
	bool _constrain_movement = false;
	// Minimum position to constrain the movement into
	int32_t _min_position = 0;
	// Maximum position to constrain the movement into
	int32_t _max_position = 0;

	// Does the movement have aceleration or it it constant speed?
	bool _accelerated_movement;

	// Current speed for the stepper
	float _current_velocity = 0;

	// Current acceleration for the stepper
	float _current_acceleration = 0;

	// Current deceleration for the stepper
	float _current_deceleration = 0;

	// Is the stepper currently sleeping?
	bool _sleeping = false;


	// Target position the stepper is moving into
	int32_t _target_position;

	// Flag indicating we need to back on the movement (when the motor is moving one way 
	// and is ordered to go the opposite way, it must slow down to a stop and then return)
	uint8_t _return_movement = false;

	// Flag indicating we should invert the DIRECTION pin value
	uint8_t _invert_movement = false;

	int32_t _target_final_position;

	int8_t _current_direction; 

	// Micros time for the previous loop movement calculations
	uint32_t _last_update_micros = 0;
};

#endif

