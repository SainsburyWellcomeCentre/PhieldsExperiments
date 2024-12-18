# PhieldsExperiments motor control requirements
## Instalation instructions
Download and install the Arduino IDE  from https://www.arduino.cc/en/software
Any version after 2.x.x should be compatible with it.

Add the libraries folder from this repo to the Arduino IDE sketchbook location (You can check the location in File->Preferences).

Open the sketch "StepperTest" and upload it to the test Arduino. The sketch should be compatible with any Arduino using the Atmega 168 or 328 chip variants. The board used in the initial tests was an Arduino Nano with an Atmega 328P (old bootloader).


## Test instructions
We can interact with the sketch sending simple serial commands using the serial console. The ';' is used as a separator

- `DROP` The Drop command will perform a drop when called. If called with a parameter ex: `D;5` will perform 5 drop tests back to back.
- `R` The Release motor command will stop the brake and the motor, allowing the spool to be moved easily by hand. THE LOOM WILL DROP, MAKE SURE IT'S BEING HELD BEFORE USING THIS.
- `E` The Engage motor command will apply the brake and lock the motor in place.
- `M` The Move command will move the motor by a specific amount of steps from the current position. It needs to receive a numeric parameter indicating the amount of steps to move and the direction: `D;200` will move the loom 200 steps down, `D;-200` will bring it back up.
- `Z` The Zero command will set the current motor position to the zero position.
- `DropMotorParameters` Set new parameters for the drop movement. Use `DropMotorParameters;MOTOR_MIN_SPEED;MOTOR_MAX_SPEED;MOTOR_DROP_ACCELERATION;MOTOR_DROP_DECELERATION`
- `ResetMotorParameters` Set new parameters for the reset movement. Use `ResetMotorParameters;MOTOR_MIN_SPEED;MOTOR_MAX_SPEED;MOTOR_RESET_ACCELERATION;MOTOR_RESET_DECELERATION`
- `LoomParameters` Set new parameters for the loom configuration movement. Use `LoomParameters;TOTAL_STEPS_TRAVELED;LOOM_BOTTOM_PAUSE;LOOM_DROP_PAUSE`
- `MotorParameters` Set new parameters for the motor hardware configuration. Use `MotorParameters;MOTOR_MICROSTEPPING_COMPENSATION;BRAKE_ENGAGE_DELAY`
