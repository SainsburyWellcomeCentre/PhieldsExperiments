#ifndef ARDUINO_STATEMACHINE_H
#define ARDUINO_STATEMACHINE_H

#include <Arduino.h>

#include <inttypes.h>

#define INVALID_STATE_ID -1

#define DEBUG_STATE_MACHINE(x) 

class StateMachine 
{
    public:
        StateMachine();
        ~StateMachine();

		// Adds a new state to the list

		// Returns the number of existing states
        uint8_t addState (	int8_t state_id, const char* name, 
							void (*on_enter)(), void (*on_run)(), void (*on_exit)(), 
							int8_t next_state_id = INVALID_STATE_ID, uint32_t next_state_delay = 0);

		// Configures a state transition for a specific state
		void setStateTransition(int8_t state_id, int8_t next_state_id, uint32_t next_state_delay);

		// Changes the state machine into a specific state
		void setState(int8_t new_state);

		// Checks if a specific amount of time has passed since the machine entered the current state
		// Returns: true if enough time has passed, false otherwise
		bool checkElapsedTime(uint32_t time);

		// Run the state machine, processing the different transitions between states
		void run();

		// Enable the printing of state transitions to a serial port
		void printStateTransitions(HardwareSerial * port = &Serial);

    private:
		struct State
		{
			public:
				int8_t state_id;
				char* state_name;
				void (*on_enter)();
				void (*on_run)();
				void (*on_exit)();
				int8_t next_state_id;
				uint32_t next_state_delay;
		};
		State* _state_list;
		uint8_t _state_count;

		// Current state of the state machine
		int8_t _current_state;

		// Starting time of entering the current state of the machine
		uint32_t _initial_state_time;

		// Serial port to print the state transitions
		HardwareSerial*  _print_state_transitions_port;
};

#endif
