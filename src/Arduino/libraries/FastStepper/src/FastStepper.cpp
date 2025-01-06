
#if defined(WIRING)
 #include <Wiring.h>
#elif ARDUINO >= 100
 #include <Arduino.h>
#else
 #include <WProgram.h>
#endif

#include "FastStepper.h"

#if defined(__AVR_ATmega8__)
#define TCCR2A TCCR2
#define TCCR2B TCCR2
#define COM2A1 COM21
#define COM2A0 COM20
#define OCR2A OCR2
#define TIMSK2 TIMSK
#define OCIE2A OCIE2
#define TIMER2_COMPA_vect TIMER2_COMP_vect
#define TIMSK1 TIMSK
#endif


#if !defined(__AVR_ATmega8__)
volatile int32_t __timer0_step_current_position = 0;
volatile int32_t __timer0_step_target_position = 0;
volatile int8_t __timer0_step_direction = 0;
volatile uint8_t *__timer0_step_pin_port;
volatile uint8_t __timer0_step_pin_mask;
#endif

volatile int32_t __timer1_step_current_position = 0;
volatile int32_t __timer1_step_target_position = 0;
volatile int8_t __timer1_step_direction = 0;
volatile uint8_t *__timer1_step_pin_port;
volatile uint8_t __timer1_step_pin_mask;

volatile int32_t __timer2_step_current_position = 0;
volatile int32_t __timer2_step_target_position = 0;
volatile int8_t __timer2_step_direction = 0;
volatile uint8_t *__timer2_step_pin_port;
volatile uint8_t __timer2_step_pin_mask;

uint8_t _new_TCCR2B;
uint8_t _new_OCR2A;

#if defined(__AVR_ATmega1280__)
volatile int32_t timer3_step_current_position = 0;
volatile int32_t timer3_step_target_position = 0;
volatile int8_t timer3_step_direction = 0;
volatile uint8_t *timer3_step_pin_port;
volatile uint8_t timer3_step_pin_mask;

volatile int32_t timer4_step_current_position = 0;
volatile int32_t timer4_step_target_position = 0;
volatile int8_t timer4_step_direction = 0;
volatile uint8_t *timer4_step_pin_port;
volatile uint8_t timer4_step_pin_mask;

volatile int32_t timer5_step_current_position = 0;
volatile int32_t timer5_step_target_position = 0;
volatile int8_t timer5_step_direction = 0;
volatile uint8_t *timer5_step_pin_port;
volatile uint8_t timer5_step_pin_mask;
#endif


#if defined(__AVR_ATmega1280__)

#define AVAILABLE_STEP_PINS 6

// Leave timers 1, and zero to last.
const uint8_t PROGMEM tone_pin_to_timer_PGM[] = { 2, 3, 4, 5, 1, 0 };

#elif defined(__AVR_ATmega8__)

#define AVAILABLE_STEP_PINS 2

const uint8_t PROGMEM tone_pin_to_timer_PGM[] = { 2, 1 };

#else

#define AVAILABLE_STEP_PINS 3

// Leave timer 0 to last.
const uint8_t PROGMEM tone_pin_to_timer_PGM[] = { 2, 1, 0 };

#endif


// Initialize our pin count
uint8_t FastStepper::_steps_pin_count = 0;


// Interrupt routines
#if !defined(__AVR_ATmega8__)
#ifdef WIRING
void Tone_Timer0_Interrupt(void)
#else
ISR(TIMER0_COMPA_vect)
#endif
{
	/*
  if (timer0_step_current_position != 0)
  {
    // toggle the pin
    *timer0_step_pin_port ^= timer0_step_pin_mask;

    if (timer0_step_current_position > 0)
      timer0_step_current_position--;
  }
  else
  {
    TIMSK0 &= ~(1 << OCIE0A);                 // disable the interrupt
    *timer0_step_pin_port &= ~(timer0_step_pin_mask);   // keep pin low after stop
  }
  */
}
#endif


#ifdef WIRING
void Tone_Timer1_Interrupt(void)
#else
ISR(TIMER1_COMPA_vect)
#endif
{

	// If we haven't reached the desired position and we're moving, let's keep stepping
	if (
		(__timer1_step_direction > 0 && __timer1_step_current_position < __timer1_step_target_position) ||
		(__timer1_step_direction < 0 && __timer1_step_current_position > __timer1_step_target_position)
		)
	{
		// toggle the pin
		*__timer1_step_pin_port ^= __timer1_step_pin_mask;

		// If the pin just went from low to high, let's count a step
		if (*__timer1_step_pin_port & __timer1_step_pin_mask)
		{
			// Adjust the position depending on the current direction
			if (__timer1_step_direction == 1) __timer1_step_current_position++;
			else __timer1_step_current_position--;
		}
	}
	// If we reached the target positon or need to stop for some other reason, stop everything
	else
	{
		TIMSK1 &= ~(1 << OCIE1A);							// disable the interrupt
		*__timer1_step_pin_port &= ~(__timer1_step_pin_mask);	// keep pin low after stop
		__timer1_step_direction = 0;							// Set the flag direction as stopped 
}

}

// 4.95uS  3.2uS


#ifdef WIRING
void Tone_Timer2_Interrupt(void)
#else
ISR(TIMER2_COMPA_vect)
#endif
{
	TCCR2B = _new_TCCR2B;
	OCR2A = _new_OCR2A;

	// PORTB = PORTB | B00000001; // Pin 8 high 
	//__asm__("nop\n\t");
	// If we haven't reached the desired position and we're moving, let's keep stepping
	if ( 
		(__timer2_step_direction > 0 && __timer2_step_current_position < __timer2_step_target_position) ||
		(__timer2_step_direction < 0 && __timer2_step_current_position > __timer2_step_target_position)
		)
	{
		// toggle the pin
		*__timer2_step_pin_port ^= __timer2_step_pin_mask;

		// If the pin just went from low to high, let's count a step
		if (*__timer2_step_pin_port & __timer2_step_pin_mask)
		{
			// Adjust the position depending on the current direction
			if (__timer2_step_direction == 1) __timer2_step_current_position++;
			else __timer2_step_current_position--;
		}
		// If it just went low, let's see if we need to change the speed
		else
		{

		}
	}
	// If we reached the target positon or need to stop for some other reason, stop everything
	else
	{
		TIMSK2 &= ~(1 << OCIE2A);							// disable the interrupt
		*__timer2_step_pin_port &= ~(__timer2_step_pin_mask);	// keep pin low after stop
		__timer2_step_direction = 0;							// Set the flag direction as stopped 
	}

	//PORTB = PORTB & B11111110; // Pin 8 low
}
	







#if defined(__AVR_ATmega1280__)

#ifdef WIRING
void Tone_Timer3_Interrupt(void)
#else
ISR(TIMER3_COMPA_vect)
#endif
{
  if (timer3_step_toggle_count != 0)
  {
    // toggle the pin
    *timer3_step_pin_port ^= timer3_step_pin_mask;

    if (timer3_step_toggle_count > 0)
      timer3_step_toggle_count--;
  }
  else
  {
    TIMSK3 &= ~(1 << OCIE3A);                 // disable the interrupt
    *timer3_step_pin_port &= ~(timer3_step_pin_mask);   // keep pin low after stop
  }
}

#ifdef WIRING
void Tone_Timer4_Interrupt(void)
#else
ISR(TIMER4_COMPA_vect)
#endif
{
  if (timer4_step_toggle_count != 0)
  {
    // toggle the pin
    *timer4_step_pin_port ^= timer4_step_pin_mask;

    if (timer4_step_toggle_count > 0)
      timer4_step_toggle_count--;
  }
  else
  {
    TIMSK4 &= ~(1 << OCIE4A);                 // disable the interrupt
    *timer4_step_pin_port &= ~(timer4_step_pin_mask);   // keep pin low after stop
  }
}

#ifdef WIRING
void Tone_Timer5_Interrupt(void)
#else
ISR(TIMER5_COMPA_vect)
#endif
{
  if (timer5_step_toggle_count != 0)
  {
    // toggle the pin
    *timer5_step_pin_port ^= timer5_step_pin_mask;

    if (timer5_step_toggle_count > 0)
      timer5_step_toggle_count--;
  }
  else
  {
    TIMSK5 &= ~(1 << OCIE5A);                 // disable the interrupt
    *timer5_step_pin_port &= ~(timer5_step_pin_mask);   // keep pin low after stop
  }
}

#endif


void FastStepper::begin(int8_t step_pin, int8_t direction_pin, int8_t sleep_pin)
{
	//Serial.print(F("begin:")); Serial.println(step_pin); delay(10);
	// If there are no more timers available, we can't initialize the controller
	if (_steps_pin_count >= AVAILABLE_STEP_PINS)
	{
		// disabled
		_timer = -1;
		return;
	}

	_step_pin = step_pin;
	_direction_pin = direction_pin;
	_sleep_pin = sleep_pin;

	pinMode(_step_pin, OUTPUT);
	pinMode(_direction_pin, OUTPUT);
	digitalWrite(_direction_pin, HIGH);

	if (_sleep_pin != -1)
	{
		pinMode(_sleep_pin, OUTPUT);
		digitalWrite(_sleep_pin, LOW);
	}

    _timer = pgm_read_byte(tone_pin_to_timer_PGM + _steps_pin_count);
    _steps_pin_count++;

	//Serial.print(F("timer:")); Serial.println(_timer);

	_direction_pin_port = portOutputRegister(digitalPinToPort(_direction_pin));
	_direction_pin_mask = digitalPinToBitMask(_direction_pin);

    // Set timer specific stuff
    // All timers in CTC mode
    // 8 bit timers will require changing prescalar values,
    // whereas 16 bit timers are set to either ck/1 or ck/64 prescalar
    switch (_timer)
    {
#if !defined(__AVR_ATmega8__)
		case 0:
        // 8 bit timer
			TCCR0A = 0;
			TCCR0B = 0;
			bitWrite(TCCR0A, WGM01, 1);
			bitWrite(TCCR0B, CS00, 1);
			__timer0_step_pin_port = portOutputRegister(digitalPinToPort(_step_pin));
			__timer0_step_pin_mask = digitalPinToBitMask(_step_pin);
			#ifdef WIRING
			Timer0.attachInterrupt(INTERRUPT_COMPARE_MATCH_A, Tone_Timer0_Interrupt);
			#endif
        break;
#endif

		case 1:
			// 16 bit timer
			TCCR1A = 0;
			TCCR1B = 0;
			bitWrite(TCCR1B, WGM12, 1);
			bitWrite(TCCR1B, CS10, 1);
			__timer1_step_pin_port = portOutputRegister(digitalPinToPort(_step_pin));
			__timer1_step_pin_mask = digitalPinToBitMask(_step_pin);
			#ifdef WIRING
			Timer1.attachInterrupt(INTERRUPT_COMPARE_MATCH_A, Tone_Timer1_Interrupt);
			#endif
        break;
		case 2:
			// 8 bit timer
			TCCR2A = 0;
			TCCR2B = 0;
			bitWrite(TCCR2A, WGM21, 1);
			bitWrite(TCCR2B, CS20, 1);
			__timer2_step_pin_port = portOutputRegister(digitalPinToPort(_step_pin));
			__timer2_step_pin_mask = digitalPinToBitMask(_step_pin);
			#ifdef WIRING
			Timer2.attachInterrupt(INTERRUPT_COMPARE_MATCH_A, Tone_Timer2_Interrupt);
			#endif
      break;

#if defined(__AVR_ATmega1280__)
		case 3:
			// 16 bit timer
			TCCR3A = 0;
			TCCR3B = 0;
			bitWrite(TCCR3B, WGM32, 1);
			bitWrite(TCCR3B, CS30, 1);
			timer3_step_pin_port = portOutputRegister(digitalPinToPort(_step_pin));
			timer3_step_pin_mask = digitalPinToBitMask(_step_pin);
			#ifdef WIRING
			Timer3.attachInterrupt(INTERRUPT_COMPARE_MATCH_A, Tone_Timer3_Interrupt);
			#endif
			break;
		case 4:
			// 16 bit timer
			TCCR4A = 0;
			TCCR4B = 0;
			bitWrite(TCCR4B, WGM42, 1);
			bitWrite(TCCR4B, CS40, 1);
			timer4_step_pin_port = portOutputRegister(digitalPinToPort(_step_pin));
			timer4_step_pin_mask = digitalPinToBitMask(_step_pin);
			#ifdef WIRING
			Timer4.attachInterrupt(INTERRUPT_COMPARE_MATCH_A, Tone_Timer4_Interrupt);
			#endif
        break;
		case 5:
			// 16 bit timer
			TCCR5A = 0;
			TCCR5B = 0;
			bitWrite(TCCR5B, WGM52, 1);
			bitWrite(TCCR5B, CS50, 1);
			timer5_step_pin_port = portOutputRegister(digitalPinToPort(_step_pin));
			timer5_step_pin_mask = digitalPinToBitMask(_step_pin);
			#ifdef WIRING
			Timer5.attachInterrupt(INTERRUPT_COMPARE_MATCH_A, Tone_Timer5_Interrupt);
			#endif
        break;
#endif
    }
}


void FastStepper::setStartEndstopPin(int8_t pin)
{
	_initial_endstop_pin = pin;
	pinMode(_initial_endstop_pin, INPUT_PULLUP);
}


void FastStepper::setFinishEndstopPin(int8_t pin)
{
	_final_endstop_pin = pin;
	pinMode(_final_endstop_pin, INPUT_PULLUP);
}


int32_t FastStepper::getVelocity()
{
	return (int32_t)_current_velocity;
}


void FastStepper::setPosition(int32_t position)
{
	switch (_timer)
	{

#if !defined(__AVR_ATmega8__)
	case 0:
		TIMSK0 &= ~(1 << OCIE0A);					// Disable the interrupt
		__timer0_step_current_position = position;	// Set the current position
		__timer0_step_target_position = position;		// Set the desired position
		break;
#endif

	case 1:
		TIMSK1 &= ~(1 << OCIE1A);					// Disable the interrupt
		__timer1_step_current_position = position;	// Set the current position
		__timer1_step_target_position = position;		// Set the desired position
		break;
	case 2:
		TIMSK2 &= ~(1 << OCIE2A);					// Disable the interrupt
		__timer2_step_current_position = position;	// Set the current position
		__timer2_step_target_position = position;		// Set the desired position
		break;

#if defined(__AVR_ATmega1280__)
	case 3:
		TIMSK3 &= ~(1 << OCIE3A);					// Disable the interrupt
		timer3_step_current_position = position;	// Set the current position
		timer3_step_target_position = position;		// Set the desired position
		break;
	case 4:
		TIMSK4 &= ~(1 << OCIE4A);					// Disable the interrupt
		timer4_step_current_position = position;	// Set the current position
		timer4_step_target_position = position;		// Set the desired position
		break;
	case 5:
		TIMSK5 &= ~(1 << OCIE5A);					// Disable the interrupt
		timer5_step_current_position = position;	// Set the current position
		timer5_step_target_position = position;		// Set the desired position
		break;
#endif
	}
}


int32_t FastStepper::getCurrentPosition()
{
	int32_t result = 0;
	switch (_timer)
	{

#if !defined(__AVR_ATmega8__)
	case 0:
		TIMSK0 &= ~(1 << OCIE0A);							    // Disable the interrupt
		result = __timer0_step_current_position;					// Get the current position
		if (__timer0_step_direction) bitWrite(TIMSK0, OCIE0A, 1);	// Enable the interrupt
		break;
#endif

	case 1:
		TIMSK1 &= ~(1 << OCIE1A);							    // Disable the interrupt
		result = __timer1_step_current_position;					// Get the current position
		if (__timer1_step_direction) bitWrite(TIMSK1, OCIE1A, 1);	// Enable the interrupt
		break;
	case 2:
		TIMSK2 &= ~(1 << OCIE2A);							    // Disable the interrupt
		result = __timer2_step_current_position;					// Get the current position
		if (__timer2_step_direction) bitWrite(TIMSK2, OCIE2A, 1);	// Enable the interrupt
		break;

#if defined(__AVR_ATmega1280__)
	case 3:
		TIMSK3 &= ~(1 << OCIE3A);							    // Disable the interrupt
		result = timer3_step_current_position;					// Get the current position
		if (timer3_step_direction) bitWrite(TIMSK3, OCIE3A, 1);	// Enable the interrupt
		break;
	case 4:
		TIMSK4 &= ~(1 << OCIE4A);							    // Disable the interrupt
		result = timer4_step_current_position;					// Get the current position
		if (timer4_step_direction) bitWrite(TIMSK4, OCIE4A, 1);	// Enable the interrupt
		break;
	case 5:
		TIMSK5 &= ~(1 << OCIE5A);							    // Disable the interrupt
		result = timer5_step_current_position;					// Get the current position
		if (timer5_step_direction) bitWrite(TIMSK5, OCIE5A, 1);	// Enable the interrupt
		break;
#endif
	}
	return result;
}


int32_t FastStepper::getTargetPosition()
{
	return _target_position;
}


// Update the timers to match the new velocity
void FastStepper::updateVelocity()
{
	if (_sleeping) return;

	//PORTB = PORTB | B00000001; // Pin 8 high 
	//PORTB = PORTB & B11111110; // Pin 8 low

	uint8_t prescalarbits = 0b001;
	uint32_t ocr = 0;

	uint16_t velocity = (uint16_t)_current_velocity;

	//Serial.println(_current_velocity);

	//Serial.print(velocity); Serial.print(' ');
	//Serial.print(_target_position); Serial.print(' ');
	//Serial.print(_current_direction); Serial.println();

	// If the timer is defined, let's process some data
	if (_timer >= 0)
	{    
		// if we are using an 8 bit timer, scan through prescalars to find the best fit
		if (_timer == 0 || _timer == 2)
		{
			ocr = F_CPU / velocity / 2 - 1;
			prescalarbits = 0b001;  // ck/1: same for both timers
			if (ocr > 255)
			{
			ocr = F_CPU / velocity / 2 / 8 - 1;
			prescalarbits = 0b010;  // ck/8: same for both timers

			if (_timer == 2 && ocr > 255)
			{
				ocr = F_CPU / velocity / 2 / 32 - 1;
				prescalarbits = 0b011;
			}

			if (ocr > 255)
			{
				ocr = F_CPU / velocity / 2 / 64 - 1;
				prescalarbits = _timer == 0 ? 0b011 : 0b100;

				if (_timer == 2 && ocr > 255)
				{
				ocr = F_CPU / velocity / 2 / 128 - 1;
				prescalarbits = 0b101;
				}

				if (ocr > 255)
				{
				ocr = F_CPU / velocity / 2 / 256 - 1;
				prescalarbits = _timer == 0 ? 0b100 : 0b110;
				if (ocr > 255)
				{
					// can't do any better than /1024
					ocr = F_CPU / velocity / 2 / 1024 - 1;
					prescalarbits = _timer == 0 ? 0b101 : 0b111;
				}
				}
			}
			}

		#if !defined(__AVR_ATmega8__)
			if (_timer == 0)
			TCCR0B = (TCCR0B & 0b11111000) | prescalarbits;
			else
		#endif
			//if (__timer2_step_direction == 0) TCCR2B = (TCCR2B & 0b11111000) | prescalarbits;
			_new_TCCR2B = (TCCR2B & 0b11111000) | prescalarbits;
		}
		else
		{
			// two choices for the 16 bit timers: ck/1 or ck/64
			ocr = F_CPU / velocity / 2 - 1;

			prescalarbits = 0b001;
			if (ocr > 0xffff)
			{
			ocr = F_CPU / velocity / 2 / 64 - 1;
			prescalarbits = 0b011;
			}

			if (_timer == 1)
			TCCR1B = (TCCR1B & 0b11111000) | prescalarbits;
		#if defined(__AVR_ATmega1280__)
			else if (_timer == 3)
			TCCR3B = (TCCR3B & 0b11111000) | prescalarbits;
			else if (_timer == 4)
			TCCR4B = (TCCR4B & 0b11111000) | prescalarbits;
			else if (_timer == 5)
			TCCR5B = (TCCR5B & 0b11111000) | prescalarbits;
		#endif

		}    

		// Set the OCR for the given timer,
		// set the toggle count,
		// then turn on the interrupts
		switch (_timer)
		{

		#if !defined(__AVR_ATmega8__)
			case 0:
				//Serial.println("BAM 0"); delay(10);
				TIMSK0 &= ~(1 << OCIE0A);                 // disable the interrupt
				OCR0A = ocr;
				__timer0_step_target_position = _target_position;
				__timer0_step_direction = _current_direction;

				// Set the direction pin to make the motor move in the desired direction
				if ((__timer0_step_direction == 1 && _invert_movement == false) ||
					(__timer0_step_direction == -1 && _invert_movement == true)
					)
					*_direction_pin_port |= _direction_pin_mask;		// direction pin high
				else
					*_direction_pin_port &= ~(_direction_pin_mask);		// direction pin low

				if(velocity>0) bitWrite(TIMSK0, OCIE0A, 1);				// Enable the interrupt
				break;
		#endif

			case 1:
				// Serial.println("BAM 1"); delay(1);
				TIMSK1 &= ~(1 << OCIE1A);                 // disable the interrupt
				OCR1A = ocr;
				__timer1_step_target_position = _target_position;
				__timer1_step_direction = _current_direction;

				// Set the direction pin to make the motor move in the desired direction
				if ((__timer1_step_direction == 1 && _invert_movement == false) ||
					(__timer1_step_direction == -1 && _invert_movement == true)
					)
					*_direction_pin_port |= _direction_pin_mask;		// direction pin high
				else
					*_direction_pin_port &= ~(_direction_pin_mask);		// direction pin low

				if (velocity > 0) bitWrite(TIMSK1, OCIE1A, 1);				// Enable the interrupt
				break;


			case 2:
				// Serial.println("T2"); delay(1);
				TIMSK2 &= ~(1 << OCIE2A);                 // disable the interrupt
				//if (__timer2_step_direction == 0) OCR2A = ocr;
				_new_OCR2A = ocr;
				__timer2_step_target_position = _target_position;
				__timer2_step_direction = _current_direction;

				// Set the direction pin to make the motor move in the desired direction
				if ((__timer2_step_direction == 1 && _invert_movement == false) ||
					(__timer2_step_direction == -1 && _invert_movement == true)
					)
					*_direction_pin_port |= _direction_pin_mask;		// direction pin high
				else
					*_direction_pin_port &= ~(_direction_pin_mask);		// direction pin low

				if (velocity > 0) bitWrite(TIMSK2, OCIE2A, 1);				// Enable the interrupt
				break;


		#if defined(__AVR_ATmega1280__)
			case 3:
			OCR3A = ocr;
			timer3_step_target_position = _target_position;
			timer3_step_direction = _movement_direction;
			bitWrite(TIMSK3, OCIE3A, 1);
			break;
			case 4:
			OCR4A = ocr;
			timer4_step_target_position = _target_position;
			timer4_step_direction = _movement_direction;
			bitWrite(TIMSK4, OCIE4A, 1);
			break;
			case 5:
			OCR5A = ocr;
			timer5_step_target_position = _target_position;
			timer5_step_direction = _movement_direction;
			bitWrite(TIMSK5, OCIE5A, 1);
			break;
		#endif

		}
	}
	//PORTB = PORTB & B11111110; // Pin 8 low

}


void FastStepper::stop()
{
	switch (_timer)
	{
	#if !defined(__AVR_ATmega8__)
	case 0:
		TIMSK0 &= ~(1 << OCIE0A);							// Disable the interrupt
		*__timer0_step_pin_port &= ~(__timer0_step_pin_mask);   // Keep pin low after stop
		__timer0_step_direction = 0;						    // Set the flag direction as stopped 
		break;
	#endif
	case 1:
		TIMSK1 &= ~(1 << OCIE1A);							// Disable the interrupt
		*__timer1_step_pin_port &= ~(__timer1_step_pin_mask);   // Keep pin low after stop
		__timer1_step_direction = 0;						    // Set the flag direction as stopped 
		break;
	case 2:
		TIMSK2 &= ~(1 << OCIE2A);							// Disable the interrupt
		*__timer2_step_pin_port &= ~(__timer2_step_pin_mask);   // Keep pin low after stop
		__timer2_step_direction = 0;						    // Set the flag direction as stopped 
		break;

	#if defined(__AVR_ATmega1280__)
	case 3:
		TIMSK3 &= ~(1 << OCIE3A);							// Disable the interrupt
		*timer3_step_pin_port &= ~(timer3_step_pin_mask);   // Keep pin low after stop
		timer3_step_direction = 0;						    // Set the flag direction as stopped 
		break;
	case 4:
		TIMSK4 &= ~(1 << OCIE4A);							// Disable the interrupt
		*timer4_step_pin_port &= ~(timer4_step_pin_mask);   // Keep pin low after stop
		timer4_step_direction = 0;						    // Set the flag direction as stopped 
		break;
	case 5:
		TIMSK5 &= ~(1 << OCIE5A);							// Disable the interrupt
		*timer5_step_pin_port &= ~(timer5_step_pin_mask);   // Keep pin low after stop
		timer5_step_direction = 0;						    // Set the flag direction as stopped 
		break;
	#endif
	}

	_current_velocity = 0;
	_current_direction = 0;
}


void FastStepper::sleep()
{
	stop();
	_sleeping = true;

	if (_sleep_pin != -1)
	{
		pinMode(_sleep_pin, OUTPUT);
		digitalWrite(_sleep_pin, HIGH);
	}
}

void FastStepper::wake()
{
	_sleeping = false;
	if (_sleep_pin != -1)
	{
		pinMode(_sleep_pin, OUTPUT);
		digitalWrite(_sleep_pin, LOW);
	}
}


// Specify if we should invert the movement of the motor
void FastStepper::invertMovement(bool invert)
{
	_invert_movement = invert;
}




int8_t FastStepper::getMovementDirection(void)
{  
	switch (_timer)
	{
	#if !defined(__AVR_ATmega8__)
	case 0:
		return __timer0_step_direction;
	#endif
		return __timer1_step_direction;
	case 2:
		return __timer2_step_direction;
		break;

	#if defined(__AVR_ATmega1280__)
	case 3:
		return timer3_step_direction;
		break;
	case 4:
		return timer4_step_direction;
		break;
	case 5:
		return timer5_step_direction;
		break;
	#endif

	}
}


// Calculate how many steps it takes to decelerate to a stop from the given speed
uint32_t FastStepper::decelerationDistance(uint32_t current_speed)
{
	//Serial.print("current_speed:"); Serial.println(current_speed);
	//Serial.print("_deceleration:"); Serial.println(_deceleration);
	//return (uint32_t)((int32_t)current_speed - (int32_t)_min_velocity)*((int32_t)current_speed + (int32_t)_min_velocity) / (2 * _deceleration);
	//return (uint32_t)(current_speed*current_speed) / (2 * (int32_t)_deceleration);
	return (uint32_t)(current_speed*current_speed) / (2 * (int32_t)_deceleration);
}



void FastStepper::setMovementSpeed(uint16_t min_speed, uint16_t max_speed)
{
	if (min_speed > max_speed)
	{
		DB_ERRORS(Serial.println(F("ERROR: min_speed must be < max_speed"));)
			while (true);
	}

	_min_velocity = min_speed;
	_max_velocity = max_speed;
}


void FastStepper::setMovementAcceleration(uint32_t acceleration, uint32_t deceleration)
{
	_acceleration = acceleration;
	_deceleration = deceleration;
}

void FastStepper::setMovementJerk(int32_t acceleration_jerk, int32_t deceleration_jerk)
{
	_acceleration_jerk = acceleration_jerk;
	_deceleration_jerk = deceleration_jerk;
}


void FastStepper::setPositionConstraints(int32_t min_position, int32_t max_position)
{
	if (min_position > max_position)
	{
		DB_ERRORS(Serial.println(F("ERROR: min_position must be < max_position"));)
		while (true);
	}
	_constrain_movement = true;
	_min_position = min_position;
	_max_position = max_position;
}


void FastStepper::home(uint16_t velocity, int8_t direction)
{
	// We cannot home if the minimum and maximum movement positions are not set
	if (!_constrain_movement)
	{
		DB_ERRORS(Serial.println(F("ERROR: cannot home if min and max positions are not set"));)
		while (true);
	}

	_homing = true;
	_current_direction = direction;
	// Make sure the velocity is within the set parameters
	_current_velocity = velocity > _min_velocity ? velocity : _min_velocity;

	// If we're homing on the initial position, assume we're near the final position and move back
	if (direction == -1)
	{
		// We cannot home in this direction if the corresponding endstop pin is not set
		if (_initial_endstop_pin == -1)
		{
			DB_ERRORS(Serial.println(F("ERROR: cannot home this way if the initial endstop is not defined"));)
			while (true);
		}

		setPosition(_max_position);
		_target_position = _min_position;
	}
	// If we're homing on the final position, assume we're near the final initial and move forward
	else
	{
		// We cannot home in this direction if the corresponding endstop pin is not set
		if (_final_endstop_pin == -1)
		{
			DB_ERRORS(Serial.println(F("ERROR: cannot home this way if the final endstop is not defined"));)
			while (true);
		}
		setPosition(_min_position);
		_target_position = _max_position;
	}
	// Homing is always done at constant speed
	_accelerated_movement = false;
	updateVelocity();
}


void FastStepper::moveTo(int32_t target_position, uint16_t velocity)
{
	// Don't do anything if the driver is sleeping
	if (_sleeping)
	{
		DB_ERRORS(Serial.println(F("ERROR: cannot move when sleeping"));)
		return;
	}

	// Don't do anything if we're homing the motor
	if (_homing) 
	{
		DB_ERRORS(Serial.println(F("ERROR: cannot move when homing"));)
		return;
	}

	// Don't do anything if the target position is outside the limits of the movement
	if (_constrain_movement && (target_position < _min_position || target_position>_max_position)) 
	{
		DB_ERRORS(Serial.println(F("ERROR: cannot move outside the limits"));)
		return;
	}

	// If we're already at the target position, don't do anything
	int32_t current_position = getCurrentPosition();

	// @TODO this can still fail when moving through the target position
	if (current_position == target_position) return;

	// Direction of the target relative to the current position
	int8_t target_direction = target_position > current_position ? 1 : -1;

	// If we pass a constant velocity parameter, we will do a constant speed movement
	// Just start moving to the target position instantlly at the specified velocity
	if (velocity != 0)
	{
		_accelerated_movement = false;
		_target_position = target_position;
		_current_direction = target_direction;
		_current_velocity = velocity;
		updateVelocity();
		return;
	}

	// Seems we got an accelerated movement, let's do some MATH!!!
	_accelerated_movement = true;
	// Distance between the current motor position and the target position
	int32_t offset_distance = target_position - current_position;

	// Distance necessary to decelerate from the current velocity to a stop
	uint32_t deceleration_distance = decelerationDistance(_current_velocity);

	int8_t current_direction = getMovementDirection();

	_return_movement = false;
	// Are we stopped or is the new target in the direction we are already moving?
	if (current_direction == 0 || current_direction == target_direction)
	{
		// Maintain the movement direction
		_current_direction = target_direction;
		// If we have distance to fully decelerate to the minimum speed and stop
		// before the new target position, all is well.
		if (current_direction == 0 || abs(offset_distance) > deceleration_distance)
		{
			_target_position = target_position;
			run();
		}
		// If we don't have enough space to stop untill the new final position, 
		// we need to invert the movement
		else
		{
			_return_movement = true;
		}
	}
	// Here the motor is moving and the target is in the opposite direction of the movement
	// So we also need to invert the movement
	else
	{
		_return_movement = true;
	}

	// Inverting the movement means we continue to move in the same direction while slowing down,
	// then move back to the final position
	if (_return_movement)
	{
		// Set a target position with enough space to decelerate
		if (current_direction == 1) _target_position = current_position + deceleration_distance;
		else _target_position = current_position - deceleration_distance;

		updateVelocity();
		// Now set final position to move into, once the deceleration is complete
		_target_final_position = target_position;
	}
}


void FastStepper::run()
{
	//PORTB = PORTB | B00000001; // Pin 8 high 
	//PORTB = PORTB & B11111110; // Pin 8 low

	//if (digitalRead(_step_pin)) return;

	// Calculate the elapsed time since the last update
	// This value needs to be updated every frame
	uint32_t now = micros();
	uint32_t elapsed_time = now - _last_update_micros;
	float seconds_elapsed_time = (float)elapsed_time / 1000000;
	_last_update_micros = now;

	// Don't do anything else if the driver is sleeping
	if (_sleeping) return;

	// Check if we hit any of the endstops, if they are defined
	if (_initial_endstop_pin != -1)
	{
		// If we're moving backwards and hit the initial endstop, we stop the
		// movement and reset the position, setting it to the minimum position 
		if (_current_direction == -1 && digitalRead(_initial_endstop_pin))
		{
			stop();
			setPosition(_min_position);
			_homing = false;
			DB_EVENTS(Serial.println ("initial_endstop_pin hit");)
			return;
		}
	}
	if (_final_endstop_pin != -1)
	{
		//Serial.print("_movement_direction:"); Serial.println(_movement_direction);

		// If we're moving forward and hit the final endstop, we only stop the movemnt
		if (_current_direction == 1 && digitalRead(_final_endstop_pin))
		{
			stop();
			setPosition(_max_position);
			_homing = false;
			DB_EVENTS(Serial.println("final_endstop_pin hit");)
			return;
		}
	}

	// If the movement has no acceleration, we don't need to calculate anything
	if (!_accelerated_movement) return;

	// Is the motor stopped?
	int8_t movement_direction = getMovementDirection();
	if (!movement_direction)
	{ 
		// Make sure the variables are clean to start a new movement anytime
		_current_velocity = 0;
		_current_acceleration = _acceleration;
		_current_deceleration = _deceleration;
		elapsed_time = 0;
		seconds_elapsed_time = 0;

		if (_return_movement)
		{
			moveTo(_target_final_position);
			return;
		}
	}

	// Get the current position of the stepper
	int32_t current_position = getCurrentPosition();


	// If we reached the target, nothing else to do here
	if (current_position == _target_position) return;

	// If we are not moving, no need to calculate anything else
	if (_current_direction == 0) return;


	// Distance between the current motor position and the target position
	int32_t offset_distance = _target_position - current_position;

	// Distance necessary to decelerate from the current velocity to a stop
	uint32_t deceleration_distance = decelerationDistance(_current_velocity);


	// Serial.print(current_position);
	// Serial.print(' ');
	// Serial.print(_current_velocity);
	// Serial.print(' ');
	// Serial.println(deceleration_distance);


	// Keep track of the acceleration status
	char accel = '=';

	// If we're moving in the opposite direction of the target, we need to invert the movement
	if ((offset_distance > 0 && _current_direction == -1) || (offset_distance < 0 && _current_direction == 1))
	{
		DB_EVENTS(Serial.println("inverting movement");)
		_current_deceleration += seconds_elapsed_time * _deceleration_jerk;
		_current_velocity -= seconds_elapsed_time * _current_deceleration;
		if (_current_velocity <= _min_velocity)
		{
			_current_direction = -_current_direction;
			_current_velocity = _min_velocity;
		}
	}
	// Here we are either stopped or already moving in the right direction
	else
	{
		// Do we have enough space to stop while maintaining the desired deceleration?
		// @TODO find a better way to compensate for the deceleration distance offset
		if ((int32_t)deceleration_distance+2 < abs(offset_distance))
		{
			// If the speed is not at the maximum yet, let's crank it up
			if (_current_velocity < _max_velocity)
			{
				_current_acceleration += seconds_elapsed_time * _acceleration_jerk;
				_current_velocity += seconds_elapsed_time * _acceleration;
				// Serial.println(_current_velocity);
				accel = '+';
			}
			//Serial.print("acc:"); Serial.println(_current_acceleration); delay(10);
		}
		// If we're too close to the final positon we need to decelerate
		else
		{
			_current_deceleration += seconds_elapsed_time * _deceleration_jerk;
			_current_velocity -= seconds_elapsed_time * _current_deceleration;
			accel = '-';
			//Serial.print("dec:"); Serial.println(_current_deceleration); delay(10);
			PORTB = PORTB | B00000001; // Pin 8 high 
			PORTB = PORTB & B11111110; // Pin 8 low
		}
	}

	// Make sure the velocity is within the limits
	_current_velocity = constrain(_current_velocity, _min_velocity, _max_velocity);

	//Serial.println(_current_velocity);

	// Apply the final calculated velocity to the motor
	updateVelocity();
}
