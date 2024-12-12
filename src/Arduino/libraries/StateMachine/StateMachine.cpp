#include "StateMachine.h"


StateMachine::StateMachine() :
		_state_list(nullptr),
		_state_count(0),
		_current_state(-1),
		_print_state_transitions_port(nullptr)
{
}


StateMachine::~StateMachine()
{
	free(_state_list);
}


uint8_t StateMachine::addState (int8_t state_id, const char* name,
	void(*on_enter)(), void(*on_run)(), void(*on_exit)(),
	int8_t next_state_id, uint32_t next_state_delay)
{
	_state_list = (State*) realloc(_state_list, (_state_count + 1) * sizeof(State));

	_state_list[_state_count].state_id = state_id;
	_state_list[_state_count].state_name = name;
	_state_list[_state_count].on_enter = on_enter;
	_state_list[_state_count].on_run = on_run;
	_state_list[_state_count].on_exit = on_exit;
	_state_list[_state_count].next_state_id = next_state_id;
	_state_list[_state_count].next_state_delay = next_state_delay;
	_state_count++;
	return _state_count;
}


void StateMachine::setStateTransition(int8_t state_id, int8_t next_state_id, uint32_t next_state_delay)
{
	_state_list[state_id].next_state_id = next_state_id;
	_state_list[state_id].next_state_delay = next_state_delay;
}


void StateMachine::setState(int8_t new_state)
{
	// If we were already in a specific state, let's check if we need to run the exit callback
	if (_current_state != -1 && _state_list[_current_state].on_exit != nullptr)
	{
		DEBUG_STATE_MACHINE(Serial.print(F("onExit:")); Serial.println(_state_list[_current_state].state_name); );
		_state_list[_current_state].on_exit();
	}

	// Now we can advance into the new state
	DEBUG_STATE_MACHINE(Serial.print(F("Exiting state:")); Serial.println(_state_list[_current_state].state_name); );
	_current_state = new_state;
	DEBUG_STATE_MACHINE(Serial.print(F("Entering state:")); Serial.println(_state_list[_current_state].state_name); );

	if (_print_state_transitions_port)
	{
		_print_state_transitions_port->print(F("Entering state:")); 
		_print_state_transitions_port->println(_state_list[_current_state].state_name);
	}
	_initial_state_time = millis();

	// We need to check if there is a enter callback to the new state and execute it too
	if (_state_list[_current_state].on_enter != nullptr)
	{
		DEBUG_STATE_MACHINE(Serial.print(F("onEnter:")); Serial.println(_state_list[_current_state].state_name); );
		_state_list[_current_state].on_enter();
	}
}

bool StateMachine::checkElapsedTime(uint32_t time)
{
	return (millis() - _initial_state_time >= time);
}


void StateMachine::run()
{
	//DEBUG_STATE_MACHINE(Serial.print(F("Running state:")); Serial.println(_state_list[_current_state].state_name); delay(20););

	// Do we have a next state defined?
	if (_state_list[_current_state].next_state_id != INVALID_STATE_ID)
	{
		// Is it time to advance into it?
		if (checkElapsedTime(_state_list[_current_state].next_state_delay))
		{
			// It's time, let's go for it
			setState(_state_list[_current_state].next_state_id);
		}
	}

	// Run the current state if the run callback is defined
	if (_state_list[_current_state].on_run != nullptr)
		_state_list[_current_state].on_run();
}


void StateMachine::printStateTransitions(HardwareSerial * port)
{
	_print_state_transitions_port = port;
}
