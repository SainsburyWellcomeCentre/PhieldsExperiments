#include "StateMachine.h"

// Create an enumeration with all the states we want to process in the system
enum TestStates: uint8_t {GreenState, YellowState, RedState};

// Create a state machine that will process the state transitions
StateMachine _my_state_machine;

void setup()
{
    Serial.begin(250000);
    Serial.println(F("Starting the StateMachine test"));

	// Add all the states to the state machine
	// It's critical to add all the states in the enumeration, by the same order they were defined in the enumeration

	_my_state_machine.addState(GreenState, "Green Light", enterGreenState, runGreenState, nullptr);
	_my_state_machine.addState(YellowState, "Yellow Light", enterYellowState, nullptr, nullptr, RedState, 2000);
	_my_state_machine.addState(RedState, "Red Light", enterRedState, nullptr, nullptr, GreenState, 3000);
	_my_state_machine.printStateTransitions(&Serial);

	_my_state_machine.setState(GreenState);

}

void loop()
{
	_my_state_machine.run();
}

void enterGreenState()
{
	//Serial.println(F("Entering the GREEN state"));
}
void runGreenState()
{
	if (_my_state_machine.checkElapsedTime(5000)) _my_state_machine.setState(YellowState);
}

void enterYellowState()
{
	//Serial.println(F("Entering the YELLOW state"));
}

void enterRedState()
{
	//Serial.println(F("Entering the RED state"));
}

