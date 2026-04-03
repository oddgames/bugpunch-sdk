/* ---------------------------------------------------------------------*
*                       paxScript.NET, version 2.7                      *
*   Copyright (c) 2005-2010 Alexander Baranovsky. All Rights Reserved   *
*                                                                       *
* THE SOURCE CODE CONTAINED HEREIN AND IN RELATED FILES IS PROVIDED     *
* TO THE REGISTERED DEVELOPER. UNDER NO CIRCUMSTANCES MAY ANY PORTION   *
* OF THE SOURCE CODE BE DISTRIBUTED, DISCLOSED OR OTHERWISE MADE        *
* AVAILABLE TO ANY THIRD PARTY WITHOUT THE EXPRESS WRITTEN CONSENT OF   *
* AUTHOR.                                                               *
*                                                                       *
* THIS COPYRIGHT NOTICE MAY NOT BE REMOVED FROM THIS FILE.              *
* --------------------------------------------------------------------- *
*/

// #define dump

using System;
using System.Collections;
using System.Reflection;
using System.IO;
using SL;

namespace PaxScript.Net
{
	#region Code Class
	/// <summary>
	/// Represents p-code.
	/// </summary>
	internal sealed class Code
	{
		/// <summary>
		/// Represents implementation of p-code operator.
		/// </summary>
		delegate void Oper();

		/// <summary>
		/// Upper bound for p-code operators.
		/// </summary>
		public const int MAX_PROC = 1000;

		/// <summary>
		/// Undocumented.
		/// </summary>
		public const int FIRST_PROG_CARD = 1000;

		/// <summary>
		/// Undocumented.
		/// </summary>
		public const int DELTA_PROG_CARD = 1000;

		/// <summary>
		/// List of p-code operators.
		/// </summary>
		public StringList Operators;

		/// <summary> Undocumented. </summary>
		public  int OP_DUMMY;
		/// <summary> Uppercase matches. </summary>
		public  int OP_UPCASE_ON;
		/// <summary> Non-uppercase matches. </summary>
		public  int OP_UPCASE_OFF;
		/// <summary> Option explicit is on. </summary>
		public  int OP_EXPLICIT_ON;
		/// <summary> Option explicit is off. </summary>
		public  int OP_EXPLICIT_OFF;
		/// <summary> Option Strict is on. </summary>
		public  int OP_STRICT_ON;
		/// <summary> Option Strict is off. </summary>
		public  int OP_STRICT_OFF;
		/// <summary> Terminates script running. </summary>
		public  int OP_HALT;
		/// <summary> Prints value. </summary>
		public  int OP_PRINT;
		/// <summary> Separates lines of source code. </summary>
		public  int OP_SEPARATOR;
		/// <summary> Processes #define directive (compile time only). </summary>
		public  int OP_DEFINE;
		/// <summary> Processes #undef directive (compile time only). </summary>
		public  int OP_UNDEF;
		/// <summary> Processes #region directive (compile time only). </summary>
		public  int OP_START_REGION;
		/// <summary> Processes #endregion directive (compile time only). </summary>
		public  int OP_END_REGION;
		/// <summary> Processes beginning of module (compile time only). </summary>
		public  int OP_BEGIN_MODULE;
		/// <summary> Processes end of module (compile time only). </summary>
		public  int OP_END_MODULE;
		/// <summary> Assigns type (compile time only). </summary>
		public  int OP_ASSIGN_TYPE;
		/// <summary> Assigns type in conditional statement (compile time only). </summary>
		public  int OP_ASSIGN_COND_TYPE;
		/// <summary> Creates type reference (compile time only). </summary>
		public  int OP_CREATE_REF_TYPE;
		/// <summary> Assigns type reference (compile time only). </summary>
		public  int OP_SET_REF_TYPE;
		/// <summary> Creates namespace (compile time only). </summary>
		public  int OP_CREATE_NAMESPACE;
		/// <summary> Creates type (compile time only). </summary>
		public  int OP_CREATE_CLASS;
		/// <summary> Ends type creation (compile time only). </summary>
		public  int OP_END_CLASS;
		/// <summary> Creates field (compile time only). </summary>
		public  int OP_CREATE_FIELD;
		/// <summary> Creates property (compile time only). </summary>
		public  int OP_CREATE_PROPERTY;
		/// <summary> Creates event (compile time only). </summary>
		public  int OP_CREATE_EVENT;
		/// <summary> Opens using block (compile time only). </summary>
		public  int OP_BEGIN_USING;
		/// <summary> Closes using block (compile time only). </summary>
		public  int OP_END_USING;
		/// <summary> Creates using alias (compile time only). </summary>
		public  int OP_CREATE_USING_ALIAS;
		/// <summary> Creates type reference (compile time only). </summary>
		public  int OP_CREATE_TYPE_REFERENCE;
		/// <summary> Adds modifier to a member (compile time only). </summary>
		public  int OP_ADD_MODIFIER;
		/// <summary> Adds ancestor to a class (compile time only). </summary>
		public  int OP_ADD_ANCESTOR;
		/// <summary> Adds underlying type to enum (compile time only). </summary>
		public  int OP_ADD_UNDERLYING_TYPE;
		/// <summary> Adds read accessor to property (compile time only). </summary>
		public  int OP_ADD_READ_ACCESSOR;
		/// <summary> Adds write accessor to property (compile time only). </summary>
		public  int OP_ADD_WRITE_ACCESSOR;
		/// <summary> Sets default property (compile time only). </summary>
		public  int OP_SET_DEFAULT;
		/// <summary> Adds add accessor to event (compile time only). </summary>
		public  int OP_ADD_ADD_ACCESSOR;
		/// <summary> Adds remove accessor to event (compile time only). </summary>
		public  int OP_ADD_REMOVE_ACCESSOR;
		/// <summary> Adds pattern method to delegate class (compile time only). </summary>
		public  int OP_ADD_PATTERN;
		/// <summary> Assigns member which implements given member (compile time only). </summary>
		public  int OP_ADD_IMPLEMENTS;
		/// <summary> Assigns member which handles given member (compile time only). </summary>
		public  int OP_ADD_HANDLES;
		/// <summary> Begins method call (compile time only). </summary>
		public  int OP_BEGIN_CALL;
		/// <summary> Evaluates identifier (compile time only). </summary>
		public  int OP_EVAL;
		/// <summary> Evaluates type identifier (compile time only). </summary>
		public  int OP_EVAL_TYPE;
		/// <summary> Evaluates base type identifier (compile time only). </summary>
		public  int OP_EVAL_BASE_TYPE;
		/// <summary> Assigns name to identifier (compile time only). </summary>
		public  int OP_ASSIGN_NAME;

		/// <summary> Adds mi value (compile time only). </summary>
		public  int OP_ADD_MIN_VALUE;
		/// <summary> Adds mi value (compile time only). </summary>
		public  int OP_ADD_MAX_VALUE;
		/// <summary> Adds array range type (compile time only). </summary>
		public  int OP_ADD_ARRAY_RANGE;
		/// <summary> Adds array index type (compile time only). </summary>
		public  int OP_ADD_ARRAY_INDEX;

		/// <summary> Create method (compile time only). </summary>
		public  int OP_CREATE_METHOD;
		/// <summary> Create entry point of method method (compile time only). </summary>
		public  int OP_INIT_METHOD;
		/// <summary> Ends creation of method (compile time only). </summary>
		public  int OP_END_METHOD;
		/// <summary> Add parameter to method (compile time only). </summary>
		public  int OP_ADD_PARAM;
		/// <summary> Add 'params' parameter to method (compile time only). </summary>
		public  int OP_ADD_PARAMS;
		/// <summary> Assigns default value to parameter (compile time only). </summary>
		public  int OP_ADD_DEFAULT_VALUE;
		/// <summary> Add 'params' parameter to method (compile time only). </summary>
		public  int OP_DECLARE_LOCAL_VARIABLE;
		public  int OP_DECLARE_LOCAL_VARIABLE_RUNTIME;
		/// <summary> Declare local variable of method (compile time only). </summary>
		public  int OP_DECLARE_LOCAL_SIMPLE;
		/// <summary> Adds explicit interface (compile time only). </summary>
		public  int OP_ADD_EXPLICIT_INTERFACE;
		/// <summary> Adds event field (compile time only). </summary>
		public  int OP_ADD_EVENT_FIELD;
		/// <summary> Calls method of base class (Compile time only) </summary>
		public  int OP_CALL_BASE;
		/// <summary> Calls method (Compile time only) </summary>
		public  int OP_CALL_SIMPLE;
		/// <summary> Checks presence of constructor of struct (Compile time only) </summary>
		public  int OP_CHECK_STRUCT_CONSTRUCTOR;
		/// <summary> Insert constructors of record types (Compile time only, Pascal) </summary>
		public  int OP_INSERT_STRUCT_CONSTRUCTORS;

		/// <summary> Init static variable </summary>
		public  int OP_INIT_STATIC_VAR;

		/// <summary> Not operation </summary>
		public  int OP_NOP;
		/// <summary> Not operation </summary>
		public  int OP_LABEL;
		/// <summary> Generalized Assignment </summary>
		public  int OP_ASSIGN;
		/// <summary> Struct Assignment </summary>
		public  int OP_ASSIGN_STRUCT;
		/// <summary> Generalized Bitwise And </summary>
		public  int OP_BITWISE_AND;
		/// <summary> Generalized Bitwise Xor </summary>
		public  int OP_BITWISE_XOR;
		/// <summary> Generalized Bitwise Or </summary>
		public  int OP_BITWISE_OR;
		/// <summary> Generalized Logical Or </summary>
		public  int OP_LOGICAL_OR;
		/// <summary> Generalized Logical And </summary>
		public  int OP_LOGICAL_AND;
		/// <summary> Generalized Addition </summary>
		public  int OP_PLUS;
		/// <summary> Generalized Increment </summary>
		public  int OP_INC;
		/// <summary> Generalized Subtraction </summary>
		public  int OP_MINUS;
		/// <summary> Generalized Decrement </summary>
		public  int OP_DEC;
		/// <summary> Generalized Multiplication </summary>
		public  int OP_MULT;
		/// <summary> Generalized Exponent </summary>
		public  int OP_EXPONENT;
		/// <summary> Generalized Modulus </summary>
		public  int OP_MOD;
		/// <summary> Generalized Division </summary>
		public  int OP_DIV;
		/// <summary> Generalized Equality </summary>
		public  int OP_EQ;
		/// <summary> Generalized Inequality </summary>
		public  int OP_NE;
		/// <summary> Generalized  Greater Than </summary>
		public  int OP_GT;
		/// <summary> Generalized Less Than </summary>
		public  int OP_LT;
		/// <summary> Generalized Greater Than Or Equal </summary>
		public  int OP_GE;
		/// <summary> Generalized Less Than Or Equal </summary>
		public  int OP_LE;
		/// <summary> Is </summary>
		public  int OP_IS;
		/// <summary> As </summary>
		public  int OP_AS;

		/// <summary> Generalized Left Shift </summary>
		public  int OP_LEFT_SHIFT;
		/// <summary> Generalized Right Shift </summary>
		public  int OP_RIGHT_SHIFT;

		/// <summary> Generalized Unary Plus </summary>
		public  int OP_UNARY_PLUS;
		/// <summary> Generalized Unary Minus </summary>
		public  int OP_UNARY_MINUS;
		/// <summary> Generalized Not </summary>
		public  int OP_NOT;
		/// <summary> Generalized Complement </summary>
		public  int OP_COMPLEMENT;
		/// <summary> Generalized True </summary>
		public  int OP_TRUE;
		/// <summary> Generalized False </summary>
		public  int OP_FALSE;

		/// <summary> Go To </summary>
		public  int OP_GO;
		/// <summary> Go To If False</summary>
		public  int OP_GO_FALSE;
		/// <summary> Go To If True</summary>
		public  int OP_GO_TRUE;
		/// <summary> Go To If Null</summary>
		public  int OP_GO_NULL;
		/// <summary> Go Start</summary>
		public  int OP_GOTO_START;
		/// <summary> Go Continue</summary>
		public  int OP_GOTO_CONTINUE;

		/// <summary> Try On </summary>
		public  int OP_TRY_ON;
		/// <summary> Try Off </summary>
		public  int OP_TRY_OFF;
		/// <summary> Throw </summary>
		public  int OP_THROW;
		/// <summary> Catch </summary>
		public  int OP_CATCH;
		/// <summary> Finally </summary>
		public  int OP_FINALLY;
		/// <summary> Discard Error </summary>
		public  int OP_DISCARD_ERROR;
		/// <summary> Exit On Error </summary>
		public  int OP_EXIT_ON_ERROR;
		/// <summary> On Error </summary>
		public  int OP_ONERROR;
		/// <summary> Resume Next </summary>
		public  int OP_RESUME;
		/// <summary> Resume Next </summary>
		public  int OP_RESUME_NEXT;

		/// <summary> Create array instance </summary>
		public  int OP_CREATE_ARRAY_INSTANCE;
		/// <summary> Create Object </summary>
		public  int OP_CREATE_OBJECT;
		/// <summary> Create Reference </summary>
		public  int OP_CREATE_REFERENCE;
		/// <summary> Setup Delegate </summary>
		public  int OP_SETUP_DELEGATE;
		/// <summary> Delegate Addition </summary>
		public  int OP_ADD_DELEGATES;
		/// <summary> Delegate Subtraction </summary>
		public  int OP_SUB_DELEGATES;
		/// <summary> Delegate Equality </summary>
		public  int OP_EQ_DELEGATES;
		/// <summary> Delegate Inequality </summary>
		public  int OP_NE_DELEGATES;
		/// <summary> Creates a delegate object </summary>
		public  int OP_ADDRESS_OF;

		/// <summary> Create Index Object </summary>
		public  int OP_CREATE_INDEX_OBJECT;
		/// <summary> Add Index To Index Object </summary>
		public  int OP_ADD_INDEX;
		/// <summary> Set Up Index Object </summary>
		public  int OP_SETUP_INDEX_OBJECT;

		/// <summary> Push Value Into Stack </summary>
		public  int OP_PUSH;
		/// <summary> Call Method </summary>
		public  int OP_CALL;
		/// <summary> Call Virtual Method </summary>
		public  int OP_CALL_VIRT;
		/// <summary> Calls host delegate </summary>
		public  int OP_DYNAMIC_INVOKE;
		/// <summary> Add Event </summary>
		public  int OP_CALL_ADD_EVENT;
		/// <summary> Raise event </summary>
		public  int OP_RAISE_EVENT;
		/// <summary> Find First Delegate </summary>
		public  int OP_FIND_FIRST_DELEGATE;
		/// <summary> Find Next Delegate </summary>
		public  int OP_FIND_NEXT_DELEGATE;
		/// <summary> Return </summary>
		public  int OP_RET;
		/// <summary> Return </summary>
		public  int OP_EXIT_SUB;
		/// <summary> Get Value Of Parameter </summary>
		public  int OP_GET_PARAM_VALUE;

        /// <summary> Redim VB array </summary>
        public int OP_REDIM;

		/// <summary> Switch On Checked State </summary>
		public  int OP_CHECKED;
		/// <summary> Restore Previous Checked State </summary>
		public  int OP_RESTORE_CHECKED_STATE;
		/// <summary> Dispose Object </summary>
		public  int OP_DISPOSE;
		/// <summary> Type Of </summary>
		public  int OP_TYPEOF;

		/// <summary> Switch On Lock State </summary>
		public  int OP_LOCK;
		/// <summary> Switch Off Lock State </summary>
		public  int OP_UNLOCK;

		/// <summary> Cast Type of Value </summary>
		public  int OP_CAST;
		/// <summary> Convert To System.SByte </summary>
		public  int OP_TO_SBYTE;
		/// <summary> Convert To System.Byte </summary>
		public  int OP_TO_BYTE;
		/// <summary> Convert To System.UInt16 </summary>
		public  int OP_TO_USHORT;
		/// <summary> Convert To System.Int16 </summary>
		public  int OP_TO_SHORT;
		/// <summary> Convert To System.UInt32 </summary>
		public  int OP_TO_UINT;
		/// <summary> Convert To System.Int32 </summary>
		public  int OP_TO_INT;
		/// <summary> Convert To System.UInt64 </summary>
		public  int OP_TO_ULONG;
		/// <summary> Convert To System.Int64 </summary>
		public  int OP_TO_LONG;
		/// <summary> Convert To System.Char </summary>
		public  int OP_TO_CHAR;
		/// <summary> Convert To System.Single </summary>
		public  int OP_TO_FLOAT;
		/// <summary> Convert To System.Double </summary>
		public  int OP_TO_DOUBLE;
		/// <summary> Convert To System.Decimal </summary>
		public  int OP_TO_DECIMAL;
		/// <summary> Convert To System.String </summary>
		public  int OP_TO_STRING;
		/// <summary> Convert To System.Boolean </summary>
		public  int OP_TO_BOOLEAN;
		/// <summary> Convert To System.Enum </summary>
		public  int OP_TO_ENUM;

		public  int OP_TO_CHAR_ARRAY;

		/// <summary>  Full List of Detailed Operators </summary>
		AssocIntegers detailed_operators;

		/// <summary>  Unary Minus Operators </summary>
		AssocIntegers detailed_negation_operators;
		/// <summary>  Negation System.Int32</summary>
		public  int OP_NEGATION_INT;
		/// <summary>  Negation System.Int64</summary>
		public  int OP_NEGATION_LONG;
		/// <summary>  Negation System.Single</summary>
		public  int OP_NEGATION_FLOAT;
		/// <summary>  Negation System.Double</summary>
		public  int OP_NEGATION_DOUBLE;
		/// <summary>  Negation System.Decimal</summary>
		public  int OP_NEGATION_DECIMAL;

		/// <summary>  Logical Negation Operators </summary>
		 AssocIntegers detailed_logical_negation_operators;
		/// <summary> Negation System.Boolean</summary>
		public  int OP_LOGICAL_NEGATION_BOOL;

		/// <summary>  Bitwise Complement Operators </summary>
		 AssocIntegers detailed_bitwise_complement_operators;
		/// <summary>  Bitwise Complement System.Int32 </summary>
		public  int OP_BITWISE_COMPLEMENT_INT;
		/// <summary>  Bitwise Complement System.UInt32 </summary>
		public  int OP_BITWISE_COMPLEMENT_UINT;
		/// <summary>  Bitwise Complement System.Int64 </summary>
		public  int OP_BITWISE_COMPLEMENT_LONG;
		/// <summary>  Bitwise Complement System.UInt64 </summary>
		public  int OP_BITWISE_COMPLEMENT_ULONG;

		/// <summary>  Increment Operators </summary>
		 AssocIntegers detailed_inc_operators;
		/// <summary>  Increment System.SByte </summary>
		public  int OP_INC_SBYTE;
		/// <summary>  Increment System.Byte </summary>
		public  int OP_INC_BYTE;
		/// <summary>  Increment System.Int16 </summary>
		public  int OP_INC_SHORT;
		/// <summary>  Increment System.UInt16 </summary>
		public  int OP_INC_USHORT;
		/// <summary>  Increment System.Int32 </summary>
		public  int OP_INC_INT;
		/// <summary>  Increment System.UInt32 </summary>
		public  int OP_INC_UINT;
		/// <summary>  Increment System.Int64 </summary>
		public  int OP_INC_LONG;
		/// <summary>  Increment System.UInt64 </summary>
		public  int OP_INC_ULONG;
		/// <summary>  Increment System.Char </summary>
		public  int OP_INC_CHAR;
		/// <summary>  Increment System.Single </summary>
		public  int OP_INC_FLOAT;
		/// <summary>  Increment System.Double </summary>
		public  int OP_INC_DOUBLE;
		/// <summary>  Increment System.Decimal </summary>
		public  int OP_INC_DECIMAL;

		/// <summary>  Decrement Operators </summary>
		 AssocIntegers detailed_dec_operators;
		/// <summary>  Decrement System.SByte </summary>
		public  int OP_DEC_SBYTE;
		/// <summary>  Decrement System.Byte </summary>
		public  int OP_DEC_BYTE;
		/// <summary>  Decrement System.Int16 </summary>
		public  int OP_DEC_SHORT;
		/// <summary>  Decrement System.UInt16 </summary>
		public  int OP_DEC_USHORT;
		/// <summary>  Decrement System.Int32 </summary>
		public  int OP_DEC_INT;
		/// <summary>  Decrement System.UInt32 </summary>
		public  int OP_DEC_UINT;
		/// <summary>  Decrement System.Int64 </summary>
		public  int OP_DEC_LONG;
		/// <summary>  Decrement System.UInt64 </summary>
		public  int OP_DEC_ULONG;
		/// <summary>  Decrement System.Char </summary>
		public  int OP_DEC_CHAR;
		/// <summary>  Decrement System.Single </summary>
		public  int OP_DEC_FLOAT;
		/// <summary>  Decrement System.Double </summary>
		public  int OP_DEC_DOUBLE;
		/// <summary>  Decrement System.Decimal </summary>
		public  int OP_DEC_DECIMAL;

		/// <summary>  Addition Operators </summary>
		 AssocIntegers detailed_addition_operators;
		/// <summary>  Addition System.Int32 </summary>
		public  int OP_ADDITION_INT;
		/// <summary>  Addition System.UInt32 </summary>
		public  int OP_ADDITION_UINT;
		/// <summary>  Addition System.Int64 </summary>
		public  int OP_ADDITION_LONG;
		/// <summary>  Addition System.UInt64 </summary>
		public  int OP_ADDITION_ULONG;
		/// <summary>  Addition System.Single </summary>
		public  int OP_ADDITION_FLOAT;
		/// <summary>  Addition System.Double </summary>
		public  int OP_ADDITION_DOUBLE;
		/// <summary>  Addition System.Decimal </summary>
		public  int OP_ADDITION_DECIMAL;
		/// <summary>  Addition System.String </summary>
		public  int OP_ADDITION_STRING;

		/// <summary>  Subtraction Operators </summary>
		 AssocIntegers detailed_subtraction_operators;
		/// <summary>  Subtraction System.Int32 </summary>
		public  int OP_SUBTRACTION_INT;
		/// <summary>  Subtraction System.UInt32 </summary>
		public  int OP_SUBTRACTION_UINT;
		/// <summary>  Subtraction System.Int64 </summary>
		public  int OP_SUBTRACTION_LONG;
		/// <summary>  Subtraction System.UInt64 </summary>
		public  int OP_SUBTRACTION_ULONG;
		/// <summary>  Subtraction System.Single </summary>
		public  int OP_SUBTRACTION_FLOAT;
		/// <summary>  Subtraction System.Double </summary>
		public  int OP_SUBTRACTION_DOUBLE;
		/// <summary>  Subtraction System.Decimal </summary>
		public  int OP_SUBTRACTION_DECIMAL;

		/// <summary>  Multiplication Operators </summary>
		 AssocIntegers detailed_multiplication_operators;
		/// <summary>  Multiplication System.Int32 </summary>
		public  int OP_MULTIPLICATION_INT;
		/// <summary>  Multiplication System.UInt32 </summary>
		public  int OP_MULTIPLICATION_UINT;
		/// <summary>  Multiplication System.Int64 </summary>
		public  int OP_MULTIPLICATION_LONG;
		/// <summary>  Multiplication System.UInt64 </summary>
		public  int OP_MULTIPLICATION_ULONG;
		/// <summary>  Multiplication System.Single </summary>
		public  int OP_MULTIPLICATION_FLOAT;
		/// <summary>  Multiplication System.Double </summary>
		public  int OP_MULTIPLICATION_DOUBLE;
		/// <summary>  Multiplication System.Decimal </summary>
		public  int OP_MULTIPLICATION_DECIMAL;

		/// <summary>  Exponent Operators </summary>
		 AssocIntegers detailed_exponent_operators;
		/// <summary>  Exponent System.Int32 </summary>
		public  int OP_EXPONENT_INT;
		/// <summary>  Exponent System.UInt32 </summary>
		public  int OP_EXPONENT_UINT;
		/// <summary>  Exponent System.Int64 </summary>
		public  int OP_EXPONENT_LONG;
		/// <summary>  Exponent System.UInt64 </summary>
		public  int OP_EXPONENT_ULONG;
		/// <summary>  Exponent System.Single </summary>
		public  int OP_EXPONENT_FLOAT;
		/// <summary>  Exponent System.Double </summary>
		public  int OP_EXPONENT_DOUBLE;
		/// <summary>  Exponent System.Decimal </summary>
		public  int OP_EXPONENT_DECIMAL;

		/// <summary>  Division Operators </summary>
		 AssocIntegers detailed_division_operators;
		/// <summary>  Division System.Int32 </summary>
		public  int OP_DIVISION_INT;
		/// <summary>  Division System.UInt32 </summary>
		public  int OP_DIVISION_UINT;
		/// <summary>  Division System.Int64 </summary>
		public  int OP_DIVISION_LONG;
		/// <summary>  Division System.UInt64 </summary>
		public  int OP_DIVISION_ULONG;
		/// <summary>  Division System.Single </summary>
		public  int OP_DIVISION_FLOAT;
		/// <summary>  Division System.Double </summary>
		public  int OP_DIVISION_DOUBLE;
		/// <summary>  Division System.Decimal </summary>
		public  int OP_DIVISION_DECIMAL;

		/// <summary>  Modulus Operators </summary>
		 AssocIntegers detailed_remainder_operators;
		/// <summary>  Modulus System.Int32 </summary>
		public  int OP_REMAINDER_INT;
		/// <summary>  Modulus System.UInt32 </summary>
		public  int OP_REMAINDER_UINT;
		/// <summary>  Modulus System.Int64 </summary>
		public  int OP_REMAINDER_LONG;
		/// <summary>  Modulus System.UInt64 </summary>
		public  int OP_REMAINDER_ULONG;
		/// <summary>  Modulus System.Single </summary>
		public  int OP_REMAINDER_FLOAT;
		/// <summary>  Modulus System.Double </summary>
		public  int OP_REMAINDER_DOUBLE;
		/// <summary>  Modulus System.Decimal </summary>
		public  int OP_REMAINDER_DECIMAL;

		/// <summary>  Left Shift Operators </summary>
		 AssocIntegers detailed_left_shift_operators;
		/// <summary>  Left Shift System.Int32 </summary>
		public  int OP_LEFT_SHIFT_INT;
		/// <summary>  Left Shift System.UInt32 </summary>
		public  int OP_LEFT_SHIFT_UINT;
		/// <summary>  Left Shift System.Int64 </summary>
		public  int OP_LEFT_SHIFT_LONG;
		/// <summary>  Left Shift System.UInt64 </summary>
		public  int OP_LEFT_SHIFT_ULONG;

		/// <summary>  Right Shift Operators </summary>
		 AssocIntegers detailed_right_shift_operators;
		/// <summary>  Right Shift System.Int32 </summary>
		public  int OP_RIGHT_SHIFT_INT;
		/// <summary>  Right Shift System.UInt32 </summary>
		public  int OP_RIGHT_SHIFT_UINT;
		/// <summary>  Right Shift System.Int64 </summary>
		public  int OP_RIGHT_SHIFT_LONG;
		/// <summary>  Right Shift System.UInt64 </summary>
		public  int OP_RIGHT_SHIFT_ULONG;

		/// <summary> Bitwise And Operators </summary>
		 AssocIntegers detailed_bitwise_and_operators;
		/// <summary> Bitwise And System.Int32 </summary>
		public  int OP_BITWISE_AND_INT;
		/// <summary> Bitwise And System.UInt32 </summary>
		public  int OP_BITWISE_AND_UINT;
		/// <summary> Bitwise And System.Int64 </summary>
		public  int OP_BITWISE_AND_LONG;
		/// <summary> Bitwise And System.UInt64 </summary>
		public  int OP_BITWISE_AND_ULONG;
		/// <summary> Bitwise And System.Boolean </summary>
		public  int OP_BITWISE_AND_BOOL;

		/// <summary> Bitwise Or Operators </summary>
		 AssocIntegers detailed_bitwise_or_operators;
		/// <summary> Bitwise Or System.Int32 </summary>
		public  int OP_BITWISE_OR_INT;
		/// <summary> Bitwise Or System.UInt32 </summary>
		public  int OP_BITWISE_OR_UINT;
		/// <summary> Bitwise Or System.Int64 </summary>
		public  int OP_BITWISE_OR_LONG;
		/// <summary> Bitwise Or System.UInt64 </summary>
		public  int OP_BITWISE_OR_ULONG;
		/// <summary> Bitwise Or System.Boolean </summary>
		public  int OP_BITWISE_OR_BOOL;

		/// <summary> Bitwise Xor Operators </summary>
		 AssocIntegers detailed_bitwise_xor_operators;
		/// <summary> Bitwise Xor System.Int32 </summary>
		public  int OP_BITWISE_XOR_INT;
		/// <summary> Bitwise Xor System.UInt32 </summary>
		public  int OP_BITWISE_XOR_UINT;
		/// <summary> Bitwise Xor System.Int64 </summary>
		public  int OP_BITWISE_XOR_LONG;
		/// <summary> Bitwise Xor System.UInt64 </summary>
		public  int OP_BITWISE_XOR_ULONG;
		/// <summary> Bitwise Xor System.Boolean </summary>
		public  int OP_BITWISE_XOR_BOOL;

		/// <summary> Bitwise Less Than Operators </summary>
		 AssocIntegers detailed_lt_operators;
		/// <summary> Bitwise Less Than System.Int32 </summary>
		public  int OP_LT_INT;
		/// <summary> Bitwise Less Than System.UInt32 </summary>
		public  int OP_LT_UINT;
		/// <summary> Bitwise Less Than System.Int64 </summary>
		public  int OP_LT_LONG;
		/// <summary> Bitwise Less Than System.UInt64 </summary>
		public  int OP_LT_ULONG;
		/// <summary> Bitwise Less Than System.Single </summary>
		public  int OP_LT_FLOAT;
		/// <summary> Bitwise Less Than System.Double </summary>
		public  int OP_LT_DOUBLE;
		/// <summary> Bitwise Less Than System.Decimal </summary>
		public  int OP_LT_DECIMAL;
		/// <summary> Bitwise Less Than System.String (VB.NET only) </summary>
		public  int OP_LT_STRING;

		/// <summary> Less Than Or Equal Operators </summary>
		 AssocIntegers detailed_le_operators;
		/// <summary> Bitwise Less Than Or Equal System.Int32 </summary>
		public  int OP_LE_INT;
		/// <summary> Bitwise Less Than Or Equal System.UInt32 </summary>
		public  int OP_LE_UINT;
		/// <summary> Bitwise Less Than Or Equal System.Int64 </summary>
		public  int OP_LE_LONG;
		/// <summary> Bitwise Less Than Or Equal System.UInt64 </summary>
		public  int OP_LE_ULONG;
		/// <summary> Bitwise Less Than Or Equal System.Single </summary>
		public  int OP_LE_FLOAT;
		/// <summary> Bitwise Less Than Or Equal System.Double </summary>
		public  int OP_LE_DOUBLE;
		/// <summary> Bitwise Less Than Or Equal System.Decimal </summary>
		public  int OP_LE_DECIMAL;
		/// <summary> Bitwise Less Than Or Equal System.String (VB.NET only) </summary>
		public  int OP_LE_STRING;

		/// <summary> Greater Than Operators </summary>
		 AssocIntegers detailed_gt_operators;
		/// <summary> Greater Than System.Int32 </summary>
		public  int OP_GT_INT;
		/// <summary> Greater Than System.UInt32 </summary>
		public  int OP_GT_UINT;
		/// <summary> Greater Than System.Int64 </summary>
		public  int OP_GT_LONG;
		/// <summary> Greater Than System.UInt64 </summary>
		public  int OP_GT_ULONG;
		/// <summary> Greater Than System.Single </summary>
		public  int OP_GT_FLOAT;
		/// <summary> Greater Than System.Double </summary>
		public  int OP_GT_DOUBLE;
		/// <summary> Greater Than System.Decimal </summary>
		public  int OP_GT_DECIMAL;
		/// <summary> Greater Than System.String (VB.NET only) </summary>
		public  int OP_GT_STRING;

		/// <summary> Greater Than Or Equal Operators </summary>
		 AssocIntegers detailed_ge_operators;
		/// <summary> Greater Than Or Equal System.Int32 </summary>
		public  int OP_GE_INT;
		/// <summary> Greater Than Or Equal System.UInt32 </summary>
		public  int OP_GE_UINT;
		/// <summary> Greater Than Or Equal System.Int64 </summary>
		public  int OP_GE_LONG;
		/// <summary> Greater Than Or Equal System.UInt64 </summary>
		public  int OP_GE_ULONG;
		/// <summary> Greater Than Or Equal System.Single </summary>
		public  int OP_GE_FLOAT;
		/// <summary> Greater Than Or Equal System.Double </summary>
		public  int OP_GE_DOUBLE;
		/// <summary> Greater Than Or Equal System.Decimal </summary>
		public  int OP_GE_DECIMAL;
		/// <summary> Greater Than Or Equal System.String (VB.NET only) </summary>
		public  int OP_GE_STRING;

		/// <summary> Equality Operators </summary>
		 AssocIntegers detailed_eq_operators;
		/// <summary> Equality System.Int32 </summary>
		public  int OP_EQ_INT;
		/// <summary> Equality System.UInt32 </summary>
		public  int OP_EQ_UINT;
		/// <summary> Equality System.Int64 </summary>
		public  int OP_EQ_LONG;
		/// <summary> Equality System.UInt64 </summary>
		public  int OP_EQ_ULONG;
		/// <summary> Equality System.Single </summary>
		public  int OP_EQ_FLOAT;
		/// <summary> Equality System.Double </summary>
		public  int OP_EQ_DOUBLE;
		/// <summary> Equality System.Decimal </summary>
		public  int OP_EQ_DECIMAL;
		/// <summary> Equality System.String </summary>
		public  int OP_EQ_STRING;
		/// <summary> Equality System.Boolean </summary>
		public  int OP_EQ_BOOL;
		/// <summary> Equality System.Object </summary>
		public  int OP_EQ_OBJECT;

		/// <summary> Inequality Operators </summary>
		 AssocIntegers detailed_ne_operators;
		/// <summary> Inequality System.Int32 </summary>
		public  int OP_NE_INT;
		/// <summary> Inequality System.UInt32 </summary>
		public  int OP_NE_UINT;
		/// <summary> Inequality System.Int64 </summary>
		public  int OP_NE_LONG;
		/// <summary> Inequality System.UInt64 </summary>
		public  int OP_NE_ULONG;
		/// <summary> Inequality System.Single </summary>
		public  int OP_NE_FLOAT;
		/// <summary> Inequality System.Double </summary>
		public  int OP_NE_DOUBLE;
		/// <summary> Inequality System.Decimal </summary>
		public  int OP_NE_DECIMAL;
		/// <summary> Inequality System.String </summary>
		public  int OP_NE_STRING;
		/// <summary> Inequality System.Boolean </summary>
		public  int OP_NE_BOOL;
		/// <summary> Inequality System.Object </summary>
		public  int OP_NE_OBJECT;

		public  int OP_SWAPPED_ARGUMENTS;

		/// <summary> Overloadable unary operators </summary>
		public  PaxHashTable overloadable_unary_operators_str;
		/// <summary> Overloadable binary operators </summary>
		public  PaxHashTable overloadable_binary_operators_str;
		
		/// <summary>
		/// Represents kernel of scripter.
		/// </summary>
		private BaseScripter scripter;

		/// <summary>
		/// Undocumented.
		/// </summary>
		private int goto_line = 0;

		/// <summary>
		/// Terminated flag.
		/// </summary>
		public bool Terminated = false;

		/// <summary>
		/// Paused flag.
		/// </summary>
		public bool Paused = false;

		/// <summary>
		/// List of operators.
		/// </summary>
		public PaxArrayList arrProc;

		/// <summary>
		/// List of p-code instructions.
		/// </summary>
		public PaxArrayList prog;

		/// <summary>
		/// Number of current p-code instruction.
		/// </summary>
		public int n;

		/// <summary>
		/// Number of instructions.
		/// </summary>
		int card;

		/// <summary>
		/// Current p-code instruction.
		/// </summary>
		public ProgRec r;

		/// <summary>
		/// Stack.
		/// </summary>
		ObjectStack stack;

		/// <summary>
		/// Stack of states.
		/// </summary>
		IntegerStack state_stack;

		/// <summary>
		/// Stack of try blocks.
		/// </summary>
		TryStack try_stack;

		/// <summary>
		/// Stack of checked states.
		/// </summary>
		ObjectStack checked_stack;

		/// <summary>
		/// Checked flag.
		/// </summary>
		bool Checked;

		/// <summary>
		/// Symbol table.
		/// </summary>
		SymbolTable symbol_table;

		/// <summary>
		/// Custom exception list.
		/// </summary>
		TypedList custom_ex_list;

		/// <summary>
		/// Custom breakpoint list.
		/// </summary>
		BreakpointList breakpoint_list;

		/// <summary>
		/// Number elements in stack.
		/// </summary>
		int curr_stack_count = 0;

		/// <summary>
		/// Stack of call records.
		/// </summary>
		public CallStack callstack;

		/// <summary>
		/// Debugging flag.
		/// </summary>
		public bool debugging = true;

		/// <summary>
		/// Resume-Next stack
		/// </summary>
		IntegerStack resume_stack;

		PaxArrayList get_item_list;

		/// <summary>
		/// Constructor.
		/// </summary>
		public Code(BaseScripter scripter)
		{
			this.scripter = scripter;
			symbol_table = scripter.symbol_table;


			Operators = new StringList(true);

			 OP_DUMMY = - Operators.Add("DUMMY");
			 OP_UPCASE_ON = - Operators.Add("UPCASE ON");
			 OP_UPCASE_OFF = - Operators.Add("UPCASE OFF");
			 OP_EXPLICIT_ON = - Operators.Add("EXPLICIT ON");
			 OP_EXPLICIT_OFF = - Operators.Add("EXPLICIT OFF");
			 OP_STRICT_ON = - Operators.Add("STRICT ON");
			 OP_STRICT_OFF = - Operators.Add("STRICT OFF");
			 OP_HALT = - Operators.Add("HALT");
			 OP_PRINT = - Operators.Add("PRINT");
			 OP_SEPARATOR = - Operators.Add("SEP");
			 OP_DEFINE = - Operators.Add("DEFINE");
			 OP_UNDEF = - Operators.Add("UNDEF");
			 OP_START_REGION = - Operators.Add("START REGION");
			 OP_END_REGION = - Operators.Add("END REGION");
			 OP_BEGIN_MODULE = - Operators.Add("BEGIN MODULE");
			 OP_END_MODULE = - Operators.Add("END MODULE");
			 OP_ASSIGN_TYPE = - Operators.Add("ASSIGN TYPE");
			 OP_ASSIGN_COND_TYPE = - Operators.Add("ASSIGN COND TYPE");
			 OP_CREATE_REF_TYPE = - Operators.Add("CREATE REF TYPE");
			 OP_SET_REF_TYPE = - Operators.Add("SET REF TYPE");
			 OP_CREATE_NAMESPACE = - Operators.Add("CREATE NAMESPACE");
			 OP_CREATE_CLASS = - Operators.Add("CREATE CLASS");
			 OP_END_CLASS = - Operators.Add("END CLASS");
			 OP_CREATE_FIELD = - Operators.Add("CREATE FIELD");
			 OP_CREATE_PROPERTY = - Operators.Add("CREATE PROPERTY");
			 OP_CREATE_EVENT = - Operators.Add("CREATE EVENT");
			 OP_BEGIN_USING = - Operators.Add("BEGIN USING");
			 OP_END_USING = - Operators.Add("END USING");
			 OP_CREATE_USING_ALIAS = - Operators.Add("CREATE USING ALIAS");
			 OP_CREATE_TYPE_REFERENCE = - Operators.Add("CREATE TYPE REFERENCE");
			 OP_ADD_MODIFIER = - Operators.Add("ADD MODIFIER");
			 OP_ADD_ANCESTOR = - Operators.Add("ADD ANCESTOR");
			 OP_ADD_UNDERLYING_TYPE = - Operators.Add("ADD UNDERLYING TYPE");
			 OP_ADD_READ_ACCESSOR = - Operators.Add("ADD READ ACCESSOR");
			 OP_ADD_WRITE_ACCESSOR = - Operators.Add("ADD WRITE ACCESSOR");
			 OP_SET_DEFAULT = - Operators.Add("SET DEFAULT");
			 OP_ADD_ADD_ACCESSOR = - Operators.Add("ADD ADD ACCESSOR");
			 OP_ADD_REMOVE_ACCESSOR = - Operators.Add("ADD REMOVE ACCESSOR");
			 OP_ADD_PATTERN = - Operators.Add("ADD PATTERN");
			 OP_ADD_IMPLEMENTS = - Operators.Add("ADD IMPLEMENTS");
			 OP_ADD_HANDLES = - Operators.Add("ADD HANDLES");
			 OP_BEGIN_CALL = - Operators.Add("BEGIN CALL");
			 OP_EVAL = - Operators.Add("EVAL");
			 OP_EVAL_TYPE = - Operators.Add("EVAL TYPE");
			 OP_EVAL_BASE_TYPE = - Operators.Add("EVAL BASE TYPE");
			 OP_ASSIGN_NAME = - Operators.Add("ASSIGN NAME");
			 OP_ADD_MIN_VALUE = - Operators.Add("ADD MIN VALUE");
			 OP_ADD_MAX_VALUE = - Operators.Add("ADD MAX VALUE");
			 OP_ADD_ARRAY_RANGE = - Operators.Add("ADD ARRAY RANGE");
			 OP_ADD_ARRAY_INDEX = - Operators.Add("ADD ARRAY INDEX");
			 OP_CREATE_METHOD = - Operators.Add("CREATE METHOD");
			 OP_INIT_METHOD = - Operators.Add("INIT METHOD");
			 OP_END_METHOD = - Operators.Add("END METHOD");
			 OP_ADD_PARAM = - Operators.Add("ADD PARAM");
			 OP_ADD_PARAMS = - Operators.Add("ADD PARAMS");
			 OP_ADD_DEFAULT_VALUE = - Operators.Add("ADD DEFAULT VALUE");
			 OP_DECLARE_LOCAL_VARIABLE = - Operators.Add("DECLARE LOCAL VARIABLE");
			 OP_DECLARE_LOCAL_VARIABLE_RUNTIME = - Operators.Add("DECLARE LOCAL VARIABLE RUNTIME");
			 OP_DECLARE_LOCAL_SIMPLE = - Operators.Add("DECLARE LOCAL SIMPLE");
			 OP_ADD_EXPLICIT_INTERFACE = - Operators.Add("ADD EXPLICIT INTERFACE");
			 OP_ADD_EVENT_FIELD = - Operators.Add("ADD EVENT FIELD");
			 OP_CALL_BASE = - Operators.Add("CALL BASE");
			 OP_CALL_SIMPLE = - Operators.Add("CALL SIMPLE");
			 OP_CHECK_STRUCT_CONSTRUCTOR = - Operators.Add("CHECK STRUCT CONSTRUCTOR");
			 OP_INSERT_STRUCT_CONSTRUCTORS = - Operators.Add("INSERT STRUCT CONSTRUCTORS");
			 OP_NOP = - Operators.Add("NOP");
			 OP_INIT_STATIC_VAR = - Operators.Add("INIT STATIC VAR");
			 OP_LABEL = - Operators.Add("LABEL");
			 OP_ASSIGN = - Operators.Add("=");
			 OP_ASSIGN_STRUCT = - Operators.Add("= (struct)");
			 OP_BITWISE_AND = - Operators.Add("&");
			 OP_BITWISE_XOR = - Operators.Add("^");
			 OP_BITWISE_OR = - Operators.Add("|");
			 OP_LOGICAL_OR = - Operators.Add("||");
			 OP_LOGICAL_AND = - Operators.Add("&&");
			 OP_PLUS = - Operators.Add("+");
			 OP_INC = - Operators.Add("++");
			 OP_MINUS = - Operators.Add("-");
			 OP_DEC = - Operators.Add("--");
			 OP_MULT = - Operators.Add("*");
			 OP_EXPONENT = - Operators.Add("EXP");
			 OP_MOD = - Operators.Add("%");
			 OP_DIV = - Operators.Add("-");
			 OP_EQ = - Operators.Add("==");
			 OP_NE = - Operators.Add("<>");
			 OP_GT = - Operators.Add(">");
			 OP_LT = - Operators.Add("<");
			 OP_GE = - Operators.Add(">=");
			 OP_LE = - Operators.Add("<=");
			 OP_IS = - Operators.Add("is");
			 OP_AS = - Operators.Add("as");
			 OP_LEFT_SHIFT = - Operators.Add("<<");
			 OP_RIGHT_SHIFT = - Operators.Add(">>");
			 OP_UNARY_PLUS = - Operators.Add("+ (unary)");
			 OP_UNARY_MINUS = - Operators.Add("- (unary)");
			 OP_NOT = - Operators.Add("not");
			 OP_COMPLEMENT = - Operators.Add("~");
			 OP_TRUE = - Operators.Add("true");
			 OP_FALSE = - Operators.Add("false");
			 OP_GO = - Operators.Add("GO");
			 OP_GO_FALSE = - Operators.Add("GO FALSE");
			 OP_GO_TRUE = - Operators.Add("GO TRUE");
			 OP_GO_NULL = - Operators.Add("GO NULL");
			 OP_GOTO_START = - Operators.Add("GOTO START");
			 OP_GOTO_CONTINUE = - Operators.Add("GOTO CONTINUE");
			 OP_TRY_ON = - Operators.Add("TRY ON");
			 OP_TRY_OFF = - Operators.Add("TRY OFF");
			 OP_THROW = - Operators.Add("THROW");
			 OP_CATCH = - Operators.Add("CATCH");
			 OP_FINALLY = - Operators.Add("FINALLY");
			 OP_DISCARD_ERROR = - Operators.Add("DISCARD ERROR");
			 OP_EXIT_ON_ERROR = - Operators.Add("EXIT ON ERROR");
			 OP_ONERROR = - Operators.Add("ON ERROR");
			 OP_RESUME = - Operators.Add("RESUME");
			 OP_RESUME_NEXT = - Operators.Add("RESUME NEXT");
			 OP_CREATE_ARRAY_INSTANCE = - Operators.Add("CREATE ARRAY INSTANCE");
			 OP_CREATE_OBJECT = - Operators.Add("CREATE OBJECT");
			 OP_CREATE_REFERENCE = - Operators.Add("CREATE REFERENCE");
			 OP_SETUP_DELEGATE = - Operators.Add("SETUP DELEGATE");
			 OP_ADD_DELEGATES = - Operators.Add("ADD DELEGATES");
			 OP_SUB_DELEGATES = - Operators.Add("SUB DELEGATES");
			 OP_EQ_DELEGATES = - Operators.Add("EQ DELEGATES");
			 OP_NE_DELEGATES = - Operators.Add("NE DELEGATES");
			 OP_ADDRESS_OF = - Operators.Add("ADDRESS OF");
			 OP_CREATE_INDEX_OBJECT = - Operators.Add("CREATE INDEX OBJECT");
			 OP_ADD_INDEX = - Operators.Add("ADD INDEX");
			 OP_SETUP_INDEX_OBJECT = - Operators.Add("SETUP INDEX OBJECT");
			 OP_PUSH = - Operators.Add("PUSH");
			 OP_CALL = - Operators.Add("CALL");
			 OP_CALL_VIRT = - Operators.Add("CALL VIRT");
			 OP_DYNAMIC_INVOKE = - Operators.Add("DYNAMIC INVOKE");
			 OP_CALL_ADD_EVENT = - Operators.Add("CALL ADD EVENT");
			 OP_RAISE_EVENT = - Operators.Add("RAISE EVENT");
			 OP_FIND_FIRST_DELEGATE = - Operators.Add("FIND FIRST DELEGATE");
			 OP_FIND_NEXT_DELEGATE = - Operators.Add("FIND NEXT DELEGATE");
			 OP_RET = - Operators.Add("RET");
			 OP_EXIT_SUB = - Operators.Add("EXIT SUB");
			 OP_GET_PARAM_VALUE = - Operators.Add("GET PARAM VALUE");
             OP_REDIM = -Operators.Add("REDIM");
             OP_CHECKED = -Operators.Add("CHECKED");
			 OP_RESTORE_CHECKED_STATE = - Operators.Add("RESTORE CHECKED STATE");
			 OP_DISPOSE = - Operators.Add("DISPOSE");
			 OP_TYPEOF = - Operators.Add("TYPEOF");
			 OP_LOCK = - Operators.Add("LOCK");
			 OP_UNLOCK = - Operators.Add("UNLOCK");
			 OP_CAST = - Operators.Add("CAST");
			 OP_TO_SBYTE = - Operators.Add("TO SBYTE");
			 OP_TO_BYTE = - Operators.Add("TO BYTE");
			 OP_TO_USHORT = - Operators.Add("TO USHORT");
			 OP_TO_SHORT = - Operators.Add("TO SHORT");
			 OP_TO_UINT = - Operators.Add("TO UINT");
			 OP_TO_INT = - Operators.Add("TO INT");
			 OP_TO_ULONG = - Operators.Add("TO ULONG");
			 OP_TO_LONG = - Operators.Add("TO LONG");
			 OP_TO_CHAR = - Operators.Add("TO CHAR");
			 OP_TO_FLOAT = - Operators.Add("TO FLOAT");
			 OP_TO_DOUBLE = - Operators.Add("TO DOUBLE");
			 OP_TO_DECIMAL = - Operators.Add("TO DECIMAL");
			 OP_TO_STRING = - Operators.Add("TO STRING");
			 OP_TO_BOOLEAN = - Operators.Add("TO BOOLEAN");
			 OP_TO_ENUM = - Operators.Add("TO ENUM");
			 OP_TO_CHAR_ARRAY = - Operators.Add("TO CHAR[]");
			 OP_NEGATION_INT = - Operators.Add("-(unary int)");
			 OP_NEGATION_LONG = - Operators.Add("-(unary long)");
			 OP_NEGATION_FLOAT = - Operators.Add("-(unary float)");
			 OP_NEGATION_DOUBLE = - Operators.Add("-(unary double)");
			 OP_NEGATION_DECIMAL = - Operators.Add("-(unary decimal)");
			 OP_LOGICAL_NEGATION_BOOL = - Operators.Add("!(bool)");
			 OP_BITWISE_COMPLEMENT_INT = - Operators.Add("~(int)");
			 OP_BITWISE_COMPLEMENT_UINT = - Operators.Add("~(uint)");
			 OP_BITWISE_COMPLEMENT_LONG = - Operators.Add("~(long)");
			 OP_BITWISE_COMPLEMENT_ULONG = - Operators.Add("~(ulong)");
			 OP_INC_SBYTE = - Operators.Add("++(sbyte)");
			 OP_INC_BYTE = - Operators.Add("++(byte)");
			 OP_INC_SHORT = - Operators.Add("++(short)");
			 OP_INC_USHORT = - Operators.Add("++(ushort)");
			 OP_INC_INT = - Operators.Add("++(int)");
			 OP_INC_UINT = - Operators.Add("++(uint)");
			 OP_INC_LONG = - Operators.Add("++(long)");
			 OP_INC_ULONG = - Operators.Add("++(ulong)");
			 OP_INC_CHAR = - Operators.Add("++(char)");
			 OP_INC_FLOAT = - Operators.Add("++(float)");
			 OP_INC_DOUBLE = - Operators.Add("++(double)");
			 OP_INC_DECIMAL = - Operators.Add("++(decimal)");
			 OP_DEC_SBYTE = - Operators.Add("--(sbyte)");
			 OP_DEC_BYTE = - Operators.Add("--(byte)");
			 OP_DEC_SHORT = - Operators.Add("--(short)");
			 OP_DEC_USHORT = - Operators.Add("--(ushort)");
			 OP_DEC_INT = - Operators.Add("--(int)");
			 OP_DEC_UINT = - Operators.Add("--(uint)");
			 OP_DEC_LONG = - Operators.Add("--(long)");
			 OP_DEC_ULONG = - Operators.Add("--(ulong)");
			 OP_DEC_CHAR = - Operators.Add("--(char)");
			 OP_DEC_FLOAT = - Operators.Add("--(float)");
			 OP_DEC_DOUBLE = - Operators.Add("--(double)");
			 OP_DEC_DECIMAL = - Operators.Add("--(decimal)");
			 OP_ADDITION_INT = - Operators.Add("+(int)");
			 OP_ADDITION_UINT = - Operators.Add("+(uint)");
			 OP_ADDITION_LONG = - Operators.Add("+(long)");
			 OP_ADDITION_ULONG = - Operators.Add("+(ulong)");
			 OP_ADDITION_FLOAT = - Operators.Add("+(float)");
			 OP_ADDITION_DOUBLE = - Operators.Add("+(double)");
			 OP_ADDITION_DECIMAL = - Operators.Add("+(decimal)");
			 OP_ADDITION_STRING = - Operators.Add("+(string)");
			 OP_SUBTRACTION_INT = - Operators.Add("-(int)");
			 OP_SUBTRACTION_UINT = - Operators.Add("-(uint)");
			 OP_SUBTRACTION_LONG = - Operators.Add("-(long)");
			 OP_SUBTRACTION_ULONG = - Operators.Add("-(ulong)");
			 OP_SUBTRACTION_FLOAT = - Operators.Add("-(float)");
			 OP_SUBTRACTION_DOUBLE = - Operators.Add("-(double)");
			 OP_SUBTRACTION_DECIMAL = - Operators.Add("-(decimal)");
			 OP_MULTIPLICATION_INT = - Operators.Add("*(int)");
			 OP_MULTIPLICATION_UINT = - Operators.Add("*(uint)");
			 OP_MULTIPLICATION_LONG = - Operators.Add("*(long)");
			 OP_MULTIPLICATION_ULONG = - Operators.Add("*(ulong)");
			 OP_MULTIPLICATION_FLOAT = - Operators.Add("*(float)");
			 OP_MULTIPLICATION_DOUBLE = - Operators.Add("*(double)");
			 OP_MULTIPLICATION_DECIMAL = - Operators.Add("*(decimal)");
			 OP_EXPONENT_INT = - Operators.Add("EXPONENT(int)");
			 OP_EXPONENT_UINT = - Operators.Add("EXPONENT(uint)");
			 OP_EXPONENT_LONG = - Operators.Add("EXPONENT(long)");
			 OP_EXPONENT_ULONG = - Operators.Add("EXPONENT(ulong)");
			 OP_EXPONENT_FLOAT = - Operators.Add("EXPONENT(float)");
			 OP_EXPONENT_DOUBLE = - Operators.Add("EXPONENT(double)");
			 OP_EXPONENT_DECIMAL = - Operators.Add("EXPONENT(decimal)");
			 OP_DIVISION_INT = - Operators.Add("/(int)");
			 OP_DIVISION_UINT = - Operators.Add("/(uint)");
			 OP_DIVISION_LONG = - Operators.Add("/(long)");
			 OP_DIVISION_ULONG = - Operators.Add("/(ulong)");
			 OP_DIVISION_FLOAT = - Operators.Add("/(float)");
			 OP_DIVISION_DOUBLE = - Operators.Add("/(double)");
			 OP_DIVISION_DECIMAL = - Operators.Add("/(decimal)");
			 OP_REMAINDER_INT = - Operators.Add("%(int)");
			 OP_REMAINDER_UINT = - Operators.Add("%(uint)");
			 OP_REMAINDER_LONG = - Operators.Add("%(long)");
			 OP_REMAINDER_ULONG = - Operators.Add("%(ulong)");
			 OP_REMAINDER_FLOAT = - Operators.Add("%(float)");
			 OP_REMAINDER_DOUBLE = - Operators.Add("%(double)");
			 OP_REMAINDER_DECIMAL = - Operators.Add("%(decimal)");
			 OP_LEFT_SHIFT_INT = - Operators.Add("<<(int)");
			 OP_LEFT_SHIFT_UINT = - Operators.Add("<<(uint)");
			 OP_LEFT_SHIFT_LONG = - Operators.Add("<<(long)");
			 OP_LEFT_SHIFT_ULONG = - Operators.Add("<<(ulong)");
			 OP_RIGHT_SHIFT_INT = - Operators.Add(">>(int)");
			 OP_RIGHT_SHIFT_UINT = - Operators.Add(">>(uint)");
			 OP_RIGHT_SHIFT_LONG = - Operators.Add(">>(long)");
			 OP_RIGHT_SHIFT_ULONG = - Operators.Add(">>(ulong)");
			 OP_BITWISE_AND_INT = - Operators.Add("&(int)");
			 OP_BITWISE_AND_UINT = - Operators.Add("&(uint)");
			 OP_BITWISE_AND_LONG = - Operators.Add("&(long)");
			 OP_BITWISE_AND_ULONG = - Operators.Add("&(ulong)");
			 OP_BITWISE_AND_BOOL = - Operators.Add("&(bool)");
			 OP_BITWISE_OR_INT = - Operators.Add("|(int)");
			 OP_BITWISE_OR_UINT = - Operators.Add("|(uint)");
			 OP_BITWISE_OR_LONG = - Operators.Add("|(long)");
			 OP_BITWISE_OR_ULONG = - Operators.Add("|(ulong)");
			 OP_BITWISE_OR_BOOL = - Operators.Add("|(bool)");
			 OP_BITWISE_XOR_INT = - Operators.Add("^(int)");
			 OP_BITWISE_XOR_UINT = - Operators.Add("^(uint)");
			 OP_BITWISE_XOR_LONG = - Operators.Add("^(long)");
			 OP_BITWISE_XOR_ULONG = - Operators.Add("^(ulong)");
			 OP_BITWISE_XOR_BOOL = - Operators.Add("^(bool)");
			 OP_LT_INT = - Operators.Add("<(int)");
			 OP_LT_UINT = - Operators.Add("<(uint)");
			 OP_LT_LONG = - Operators.Add("<(long)");
			 OP_LT_ULONG = - Operators.Add("<(ulong)");
			 OP_LT_FLOAT = - Operators.Add("<(float)");
			 OP_LT_DOUBLE = - Operators.Add("<(double)");
			 OP_LT_DECIMAL = - Operators.Add("<(decimal)");
			 OP_LT_STRING = - Operators.Add("<(string)");
			 OP_LE_INT = - Operators.Add("<=(int)");
			 OP_LE_UINT = - Operators.Add("<=(uint)");
			 OP_LE_LONG = - Operators.Add("<=(long)");
			 OP_LE_ULONG = - Operators.Add("<=(ulong)");
			 OP_LE_FLOAT = - Operators.Add("<=(float)");
			 OP_LE_DOUBLE = - Operators.Add("<=(double)");
			 OP_LE_DECIMAL = - Operators.Add("<=(decimal)");
			 OP_LE_STRING = - Operators.Add("<=(string)");
			 OP_GT_INT = - Operators.Add(">(int)");
			 OP_GT_UINT = - Operators.Add(">(uint)");
			 OP_GT_LONG = - Operators.Add(">(long)");
			 OP_GT_ULONG = - Operators.Add(">(ulong)");
			 OP_GT_FLOAT = - Operators.Add(">(float)");
			 OP_GT_DOUBLE = - Operators.Add(">(double)");
			 OP_GT_DECIMAL = - Operators.Add(">(decimal)");
			 OP_GT_STRING = - Operators.Add(">(string)");
			 OP_GE_INT = - Operators.Add(">=(int)");
			 OP_GE_UINT = - Operators.Add(">=(uint)");
			 OP_GE_LONG = - Operators.Add(">=(long)");
			 OP_GE_ULONG = - Operators.Add(">=(ulong)");
			 OP_GE_FLOAT = - Operators.Add(">=(float)");
			 OP_GE_DOUBLE = - Operators.Add(">=(double)");
			 OP_GE_DECIMAL = - Operators.Add(">=(decimal)");
			 OP_GE_STRING = - Operators.Add(">=(string)");
			 OP_EQ_INT = - Operators.Add("==(int)");
			 OP_EQ_UINT = - Operators.Add("==(uint)");
			 OP_EQ_LONG = - Operators.Add("==(long)");
			 OP_EQ_ULONG = - Operators.Add("==(ulong)");
			 OP_EQ_FLOAT = - Operators.Add("==(float)");
			 OP_EQ_DOUBLE = - Operators.Add("==(double)");
			 OP_EQ_DECIMAL = - Operators.Add("==(decimal)");
			 OP_EQ_STRING = - Operators.Add("==(string)");
			 OP_EQ_BOOL = - Operators.Add("==(bool)");
			 OP_EQ_OBJECT = - Operators.Add("==(object)");
			 OP_NE_INT = - Operators.Add("!=(int)");
			 OP_NE_UINT = - Operators.Add("!=(uint)");
			 OP_NE_LONG = - Operators.Add("!=(long)");
			 OP_NE_ULONG = - Operators.Add("!=(ulong)");
			 OP_NE_FLOAT = - Operators.Add("!=(float)");
			 OP_NE_DOUBLE = - Operators.Add("!=(double)");
			 OP_NE_DECIMAL = - Operators.Add("!=(decimal)");
			 OP_NE_STRING = - Operators.Add("!=(string)");
			 OP_NE_BOOL = - Operators.Add("!=(bool)");
			 OP_NE_OBJECT = - Operators.Add("!=(object)");
			 OP_SWAPPED_ARGUMENTS = - Operators.Add("SWAPPED ARG");

			overloadable_unary_operators_str = new PaxHashTable();
			overloadable_unary_operators_str.Add(OP_UNARY_PLUS, "op_UnaryPlus");
			overloadable_unary_operators_str.Add(OP_UNARY_MINUS, "op_UnaryNegation");
			overloadable_unary_operators_str.Add(OP_NOT, "op_LogicalNot");
			overloadable_unary_operators_str.Add(OP_COMPLEMENT, "op_OnesComplement");
			overloadable_unary_operators_str.Add(OP_INC, "op_Increment");
			overloadable_unary_operators_str.Add(OP_DEC, "op_Decrement");
			overloadable_unary_operators_str.Add(OP_TRUE, "op_True");
			overloadable_unary_operators_str.Add(OP_FALSE, "op_False");

			overloadable_binary_operators_str = new PaxHashTable();
			overloadable_binary_operators_str.Add(OP_PLUS, "op_Addition");
			overloadable_binary_operators_str.Add(OP_MINUS, "op_Subtraction");
			overloadable_binary_operators_str.Add(OP_MULT, "op_Multiply");
			overloadable_binary_operators_str.Add(OP_DIV, "op_Division");
			overloadable_binary_operators_str.Add(OP_MOD, "op_Modulus");
			overloadable_binary_operators_str.Add(OP_BITWISE_AND, "op_BitwiseAnd");
			overloadable_binary_operators_str.Add(OP_BITWISE_OR, "op_BitwiseOr");
			overloadable_binary_operators_str.Add(OP_BITWISE_XOR, "op_ExclusiveOr");
			overloadable_binary_operators_str.Add(OP_LEFT_SHIFT, "op_LeftShift");
			overloadable_binary_operators_str.Add(OP_RIGHT_SHIFT, "op_RightShift");
			overloadable_binary_operators_str.Add(OP_EQ, "op_Equality");
			overloadable_binary_operators_str.Add(OP_NE, "op_Inequality");
			overloadable_binary_operators_str.Add(OP_GT, "op_GreaterThan");
			overloadable_binary_operators_str.Add(OP_LT, "op_LessThan");
			overloadable_binary_operators_str.Add(OP_GE, "op_GreaterThanOrEqual");
			overloadable_binary_operators_str.Add(OP_LE, "op_LessThanOrEqual");

			detailed_operators = new AssocIntegers(200);

			////////// DETAILED NEGATION OPERATORS /////////////////
			detailed_negation_operators = new AssocIntegers(5);
			detailed_negation_operators.Add((int)StandardType.Int, OP_NEGATION_INT);
			detailed_negation_operators.Add((int)StandardType.Long, OP_NEGATION_LONG);
			detailed_negation_operators.Add((int)StandardType.Float, OP_NEGATION_FLOAT);
			detailed_negation_operators.Add((int)StandardType.Double, OP_NEGATION_DOUBLE);
			detailed_negation_operators.Add((int)StandardType.Decimal, OP_NEGATION_DECIMAL);
			detailed_operators.AddFrom(detailed_negation_operators);

			////////// DETAILED LOGICAL NEGATION OPERATORS /////////////////
			detailed_logical_negation_operators = new AssocIntegers(1);
			detailed_logical_negation_operators.Add((int)StandardType.Bool, OP_LOGICAL_NEGATION_BOOL);
			detailed_operators.AddFrom(detailed_logical_negation_operators);

			////////// DETAILED BITWISE COMPLEMENT OPERATORS /////////////////
			detailed_bitwise_complement_operators = new AssocIntegers(4);
			detailed_bitwise_complement_operators.Add((int)StandardType.Int, OP_BITWISE_COMPLEMENT_INT);
			detailed_bitwise_complement_operators.Add((int)StandardType.Uint, OP_BITWISE_COMPLEMENT_UINT);
			detailed_bitwise_complement_operators.Add((int)StandardType.Long, OP_BITWISE_COMPLEMENT_LONG);
			detailed_bitwise_complement_operators.Add((int)StandardType.Ulong, OP_BITWISE_COMPLEMENT_ULONG);
			detailed_operators.AddFrom(detailed_bitwise_complement_operators);

			////////// DETAILED INC OPERATORS /////////////////
			detailed_inc_operators = new AssocIntegers(12);
			detailed_inc_operators.Add((int)StandardType.Sbyte, OP_INC_SBYTE);
			detailed_inc_operators.Add((int)StandardType.Byte, OP_INC_BYTE);
			detailed_inc_operators.Add((int)StandardType.Short, OP_INC_SHORT);
			detailed_inc_operators.Add((int)StandardType.Ushort, OP_INC_USHORT);
			detailed_inc_operators.Add((int)StandardType.Int, OP_INC_INT);
			detailed_inc_operators.Add((int)StandardType.Uint, OP_INC_UINT);
			detailed_inc_operators.Add((int)StandardType.Long, OP_INC_LONG);
			detailed_inc_operators.Add((int)StandardType.Ulong, OP_INC_ULONG);
			detailed_inc_operators.Add((int)StandardType.Char, OP_INC_CHAR);
			detailed_inc_operators.Add((int)StandardType.Float, OP_INC_FLOAT);
			detailed_inc_operators.Add((int)StandardType.Double, OP_INC_DOUBLE);
			detailed_inc_operators.Add((int)StandardType.Decimal, OP_INC_DECIMAL);
			detailed_operators.AddFrom(detailed_inc_operators);

			////////// DETAILED DEC OPERATORS /////////////////
			detailed_dec_operators = new AssocIntegers(12);
			detailed_dec_operators.Add((int)StandardType.Sbyte, OP_DEC_SBYTE);
			detailed_dec_operators.Add((int)StandardType.Byte, OP_DEC_BYTE);
			detailed_dec_operators.Add((int)StandardType.Short, OP_DEC_SHORT);
			detailed_dec_operators.Add((int)StandardType.Ushort, OP_DEC_USHORT);
			detailed_dec_operators.Add((int)StandardType.Int, OP_DEC_INT);
			detailed_dec_operators.Add((int)StandardType.Uint, OP_DEC_UINT);
			detailed_dec_operators.Add((int)StandardType.Long, OP_DEC_LONG);
			detailed_dec_operators.Add((int)StandardType.Ulong, OP_DEC_ULONG);
			detailed_dec_operators.Add((int)StandardType.Char, OP_DEC_CHAR);
			detailed_dec_operators.Add((int)StandardType.Float, OP_DEC_FLOAT);
			detailed_dec_operators.Add((int)StandardType.Double, OP_DEC_DOUBLE);
			detailed_dec_operators.Add((int)StandardType.Decimal, OP_DEC_DECIMAL);
			detailed_operators.AddFrom(detailed_dec_operators);

			////////// DETAILED ADDITION OPERATORS /////////////////
			detailed_addition_operators = new AssocIntegers(8);
			detailed_addition_operators.Add((int)StandardType.Int, OP_ADDITION_INT);
			detailed_addition_operators.Add((int)StandardType.Uint, OP_ADDITION_UINT);
			detailed_addition_operators.Add((int)StandardType.Long, OP_ADDITION_LONG);
			detailed_addition_operators.Add((int)StandardType.Ulong, OP_ADDITION_ULONG);
			detailed_addition_operators.Add((int)StandardType.Float, OP_ADDITION_FLOAT);
			detailed_addition_operators.Add((int)StandardType.Double, OP_ADDITION_DOUBLE);
			detailed_addition_operators.Add((int)StandardType.Decimal, OP_ADDITION_DECIMAL);
			detailed_addition_operators.Add((int)StandardType.String, OP_ADDITION_STRING);
			detailed_operators.AddFrom(detailed_addition_operators);

			////////// DETAILED SUBTRACTION OPERATORS /////////////////
			detailed_subtraction_operators = new AssocIntegers(7);
			detailed_subtraction_operators.Add((int)StandardType.Int, OP_SUBTRACTION_INT);
			detailed_subtraction_operators.Add((int)StandardType.Uint, OP_SUBTRACTION_UINT);
			detailed_subtraction_operators.Add((int)StandardType.Long, OP_SUBTRACTION_LONG);
			detailed_subtraction_operators.Add((int)StandardType.Ulong, OP_SUBTRACTION_ULONG);
			detailed_subtraction_operators.Add((int)StandardType.Float, OP_SUBTRACTION_FLOAT);
			detailed_subtraction_operators.Add((int)StandardType.Double, OP_SUBTRACTION_DOUBLE);
			detailed_subtraction_operators.Add((int)StandardType.Decimal, OP_SUBTRACTION_DECIMAL);
			detailed_operators.AddFrom(detailed_subtraction_operators);

			////////// DETAILED MULTIPLICATION OPERATORS /////////////////
			detailed_multiplication_operators = new AssocIntegers(7);
			detailed_multiplication_operators.Add((int)StandardType.Int, OP_MULTIPLICATION_INT);
			detailed_multiplication_operators.Add((int)StandardType.Uint, OP_MULTIPLICATION_UINT);
			detailed_multiplication_operators.Add((int)StandardType.Long, OP_MULTIPLICATION_LONG);
			detailed_multiplication_operators.Add((int)StandardType.Ulong, OP_MULTIPLICATION_ULONG);
			detailed_multiplication_operators.Add((int)StandardType.Float, OP_MULTIPLICATION_FLOAT);
			detailed_multiplication_operators.Add((int)StandardType.Double, OP_MULTIPLICATION_DOUBLE);
			detailed_multiplication_operators.Add((int)StandardType.Decimal, OP_MULTIPLICATION_DECIMAL);
			detailed_operators.AddFrom(detailed_multiplication_operators);

			////////// DETAILED EXPONENT OPERATORS /////////////////
			detailed_exponent_operators = new AssocIntegers(7);
			detailed_exponent_operators.Add((int)StandardType.Int, OP_EXPONENT_INT);
			detailed_exponent_operators.Add((int)StandardType.Uint, OP_EXPONENT_UINT);
			detailed_exponent_operators.Add((int)StandardType.Long, OP_EXPONENT_LONG);
			detailed_exponent_operators.Add((int)StandardType.Ulong, OP_EXPONENT_ULONG);
			detailed_exponent_operators.Add((int)StandardType.Float, OP_EXPONENT_FLOAT);
			detailed_exponent_operators.Add((int)StandardType.Double, OP_EXPONENT_DOUBLE);
			detailed_exponent_operators.Add((int)StandardType.Decimal, OP_EXPONENT_DECIMAL);
			detailed_operators.AddFrom(detailed_exponent_operators);

			////////// DETAILED DIVISION OPERATORS /////////////////
			detailed_division_operators = new AssocIntegers(7);
			detailed_division_operators.Add((int)StandardType.Int, OP_DIVISION_INT);
			detailed_division_operators.Add((int)StandardType.Uint, OP_DIVISION_UINT);
			detailed_division_operators.Add((int)StandardType.Long, OP_DIVISION_LONG);
			detailed_division_operators.Add((int)StandardType.Ulong, OP_DIVISION_ULONG);
			detailed_division_operators.Add((int)StandardType.Float, OP_DIVISION_FLOAT);
			detailed_division_operators.Add((int)StandardType.Double, OP_DIVISION_DOUBLE);
			detailed_division_operators.Add((int)StandardType.Decimal, OP_DIVISION_DECIMAL);
			detailed_operators.AddFrom(detailed_division_operators);

			////////// DETAILED REMAINDER OPERATORS /////////////////
			detailed_remainder_operators = new AssocIntegers(7);
			detailed_remainder_operators.Add((int)StandardType.Int, OP_REMAINDER_INT);
			detailed_remainder_operators.Add((int)StandardType.Uint, OP_REMAINDER_UINT);
			detailed_remainder_operators.Add((int)StandardType.Long, OP_REMAINDER_LONG);
			detailed_remainder_operators.Add((int)StandardType.Ulong, OP_REMAINDER_ULONG);
			detailed_remainder_operators.Add((int)StandardType.Float, OP_REMAINDER_FLOAT);
			detailed_remainder_operators.Add((int)StandardType.Double, OP_REMAINDER_DOUBLE);
			detailed_remainder_operators.Add((int)StandardType.Decimal, OP_REMAINDER_DECIMAL);
			detailed_operators.AddFrom(detailed_remainder_operators);

			////////// DETAILED LEFT SHIFT OPERATORS /////////////////
			detailed_left_shift_operators = new AssocIntegers(4);
			detailed_left_shift_operators.Add((int)StandardType.Int, OP_LEFT_SHIFT_INT);
			detailed_left_shift_operators.Add((int)StandardType.Uint, OP_LEFT_SHIFT_UINT);
			detailed_left_shift_operators.Add((int)StandardType.Long, OP_LEFT_SHIFT_LONG);
			detailed_left_shift_operators.Add((int)StandardType.Ulong, OP_LEFT_SHIFT_ULONG);
			detailed_operators.AddFrom(detailed_left_shift_operators);

			////////// DETAILED RIGHT SHIFT OPERATORS /////////////////
			detailed_right_shift_operators = new AssocIntegers(4);
			detailed_right_shift_operators.Add((int)StandardType.Int, OP_RIGHT_SHIFT_INT);
			detailed_right_shift_operators.Add((int)StandardType.Uint, OP_RIGHT_SHIFT_UINT);
			detailed_right_shift_operators.Add((int)StandardType.Long, OP_RIGHT_SHIFT_LONG);
			detailed_right_shift_operators.Add((int)StandardType.Ulong, OP_RIGHT_SHIFT_ULONG);
			detailed_operators.AddFrom(detailed_right_shift_operators);

			////////// DETAILED BITWISE AND OPERATORS /////////////////
			detailed_bitwise_and_operators = new AssocIntegers(5);
			detailed_bitwise_and_operators.Add((int)StandardType.Int, OP_BITWISE_AND_INT);
			detailed_bitwise_and_operators.Add((int)StandardType.Uint, OP_BITWISE_AND_UINT);
			detailed_bitwise_and_operators.Add((int)StandardType.Long, OP_BITWISE_AND_LONG);
			detailed_bitwise_and_operators.Add((int)StandardType.Ulong, OP_BITWISE_AND_ULONG);
			detailed_bitwise_and_operators.Add((int)StandardType.Bool, OP_BITWISE_AND_BOOL);
			detailed_operators.AddFrom(detailed_bitwise_and_operators);

			////////// DETAILED BITWISE OR OPERATORS /////////////////
			detailed_bitwise_or_operators = new AssocIntegers(5);
			detailed_bitwise_or_operators.Add((int)StandardType.Int, OP_BITWISE_OR_INT);
			detailed_bitwise_or_operators.Add((int)StandardType.Uint, OP_BITWISE_OR_UINT);
			detailed_bitwise_or_operators.Add((int)StandardType.Long, OP_BITWISE_OR_LONG);
			detailed_bitwise_or_operators.Add((int)StandardType.Ulong, OP_BITWISE_OR_ULONG);
			detailed_bitwise_or_operators.Add((int)StandardType.Bool, OP_BITWISE_OR_BOOL);
			detailed_operators.AddFrom(detailed_bitwise_or_operators);

			////////// DETAILED BITWISE XOR OPERATORS /////////////////
			detailed_bitwise_xor_operators = new AssocIntegers(5);
			detailed_bitwise_xor_operators.Add((int)StandardType.Int, OP_BITWISE_XOR_INT);
			detailed_bitwise_xor_operators.Add((int)StandardType.Uint, OP_BITWISE_XOR_UINT);
			detailed_bitwise_xor_operators.Add((int)StandardType.Long, OP_BITWISE_XOR_LONG);
			detailed_bitwise_xor_operators.Add((int)StandardType.Ulong, OP_BITWISE_XOR_ULONG);
			detailed_bitwise_xor_operators.Add((int)StandardType.Bool, OP_BITWISE_XOR_BOOL);
			detailed_operators.AddFrom(detailed_bitwise_xor_operators);

			////////// DETAILED LT OPERATORS /////////////////
			detailed_lt_operators = new AssocIntegers(8);
			detailed_lt_operators.Add((int)StandardType.Int, OP_LT_INT);
			detailed_lt_operators.Add((int)StandardType.Uint, OP_LT_UINT);
			detailed_lt_operators.Add((int)StandardType.Long, OP_LT_LONG);
			detailed_lt_operators.Add((int)StandardType.Ulong, OP_LT_ULONG);
			detailed_lt_operators.Add((int)StandardType.Float, OP_LT_FLOAT);
			detailed_lt_operators.Add((int)StandardType.Double, OP_LT_DOUBLE);
			detailed_lt_operators.Add((int)StandardType.Decimal, OP_LT_DECIMAL);
			detailed_lt_operators.Add((int)StandardType.String, OP_LT_STRING);
			detailed_operators.AddFrom(detailed_lt_operators);

			////////// DETAILED LE OPERATORS /////////////////
			detailed_le_operators = new AssocIntegers(8);
			detailed_le_operators.Add((int)StandardType.Int, OP_LE_INT);
			detailed_le_operators.Add((int)StandardType.Uint, OP_LE_UINT);
			detailed_le_operators.Add((int)StandardType.Long, OP_LE_LONG);
			detailed_le_operators.Add((int)StandardType.Ulong, OP_LE_ULONG);
			detailed_le_operators.Add((int)StandardType.Float, OP_LE_FLOAT);
			detailed_le_operators.Add((int)StandardType.Double, OP_LE_DOUBLE);
			detailed_le_operators.Add((int)StandardType.Decimal, OP_LE_DECIMAL);
			detailed_le_operators.Add((int)StandardType.String, OP_LE_STRING);
			detailed_operators.AddFrom(detailed_le_operators);

			////////// DETAILED GT OPERATORS /////////////////
			detailed_gt_operators = new AssocIntegers(8);
			detailed_gt_operators.Add((int)StandardType.Int, OP_GT_INT);
			detailed_gt_operators.Add((int)StandardType.Uint, OP_GT_UINT);
			detailed_gt_operators.Add((int)StandardType.Long, OP_GT_LONG);
			detailed_gt_operators.Add((int)StandardType.Ulong, OP_GT_ULONG);
			detailed_gt_operators.Add((int)StandardType.Float, OP_GT_FLOAT);
			detailed_gt_operators.Add((int)StandardType.Double, OP_GT_DOUBLE);
			detailed_gt_operators.Add((int)StandardType.Decimal, OP_GT_DECIMAL);
			detailed_gt_operators.Add((int)StandardType.String, OP_GT_STRING);
			detailed_operators.AddFrom(detailed_gt_operators);

			////////// DETAILED GE OPERATORS /////////////////
			detailed_ge_operators = new AssocIntegers(8);
			detailed_ge_operators.Add((int)StandardType.Int, OP_GE_INT);
			detailed_ge_operators.Add((int)StandardType.Uint, OP_GE_UINT);
			detailed_ge_operators.Add((int)StandardType.Long, OP_GE_LONG);
			detailed_ge_operators.Add((int)StandardType.Ulong, OP_GE_ULONG);
			detailed_ge_operators.Add((int)StandardType.Float, OP_GE_FLOAT);
			detailed_ge_operators.Add((int)StandardType.Double, OP_GE_DOUBLE);
			detailed_ge_operators.Add((int)StandardType.Decimal, OP_GE_DECIMAL);
			detailed_ge_operators.Add((int)StandardType.String, OP_GE_STRING);
			detailed_operators.AddFrom(detailed_ge_operators);

			////////// DETAILED EQ OPERATORS /////////////////
			detailed_eq_operators = new AssocIntegers(10);
			detailed_eq_operators.Add((int)StandardType.Int, OP_EQ_INT);
			detailed_eq_operators.Add((int)StandardType.Uint, OP_EQ_UINT);
			detailed_eq_operators.Add((int)StandardType.Long, OP_EQ_LONG);
			detailed_eq_operators.Add((int)StandardType.Ulong, OP_EQ_ULONG);
			detailed_eq_operators.Add((int)StandardType.Float, OP_EQ_FLOAT);
			detailed_eq_operators.Add((int)StandardType.Double, OP_EQ_DOUBLE);
			detailed_eq_operators.Add((int)StandardType.Decimal, OP_EQ_DECIMAL);
			detailed_eq_operators.Add((int)StandardType.String, OP_EQ_STRING);
			detailed_eq_operators.Add((int)StandardType.Bool, OP_EQ_BOOL);
			detailed_eq_operators.Add((int)StandardType.Object, OP_EQ_OBJECT);
			detailed_operators.AddFrom(detailed_eq_operators);

			////////// DETAILED NE OPERATORS /////////////////
			detailed_ne_operators = new AssocIntegers(10);
			detailed_ne_operators.Add((int)StandardType.Int, OP_NE_INT);
			detailed_ne_operators.Add((int)StandardType.Uint, OP_NE_UINT);
			detailed_ne_operators.Add((int)StandardType.Long, OP_NE_LONG);
			detailed_ne_operators.Add((int)StandardType.Ulong, OP_NE_ULONG);
			detailed_ne_operators.Add((int)StandardType.Float, OP_NE_FLOAT);
			detailed_ne_operators.Add((int)StandardType.Double, OP_NE_DOUBLE);
			detailed_ne_operators.Add((int)StandardType.Decimal, OP_NE_DECIMAL);
			detailed_ne_operators.Add((int)StandardType.String, OP_NE_STRING);
			detailed_ne_operators.Add((int)StandardType.Bool, OP_NE_BOOL);
			detailed_ne_operators.Add((int)StandardType.Object, OP_NE_OBJECT);
			detailed_operators.AddFrom(detailed_ne_operators);

			stack = new ObjectStack();
			state_stack = new IntegerStack();
			try_stack = new TryStack();
			checked_stack = new ObjectStack();
			custom_ex_list = new TypedList(false);
			breakpoint_list = new BreakpointList(scripter);
			callstack = new CallStack(scripter);
			resume_stack = new IntegerStack();

			get_item_list = new PaxArrayList();

			arrProc = new PaxArrayList();
			int i;
			for (i=0; i<MAX_PROC; i++)
			  arrProc.Add(null);

			arrProc[-OP_BEGIN_MODULE] = new Oper(OperNop);
			arrProc[-OP_END_MODULE] = new Oper(OperNop);

			arrProc[-OP_CHECKED] = new Oper(OperChecked);
			arrProc[-OP_RESTORE_CHECKED_STATE] = new Oper(OperRestoreCheckedState);

			arrProc[-OP_LOCK] = new Oper(OperLock);
			arrProc[-OP_UNLOCK] = new Oper(OperUnlock);

			arrProc[-OP_DISPOSE] = new Oper(OperDispose);
			arrProc[-OP_TYPEOF] = new Oper(OperTypeOf);

			arrProc[-OP_CREATE_CLASS] = new Oper(OperCreateClass);
			arrProc[-OP_END_CLASS] = new Oper(OperNop);
			arrProc[-OP_CREATE_OBJECT] = new Oper(OperCreateObject);
			arrProc[-OP_CREATE_REFERENCE] = new Oper(OperCreateReference);
			arrProc[-OP_CREATE_ARRAY_INSTANCE] = new Oper(OperCreateArrayInstance);

			arrProc[-OP_SETUP_DELEGATE] = new Oper(OperSetupDelegate);
			arrProc[-OP_ADD_DELEGATES] = new Oper(OperAddDelegates);
			arrProc[-OP_SUB_DELEGATES] = new Oper(OperSubDelegates);
			arrProc[-OP_EQ_DELEGATES] = new Oper(OperEqDelegates);
			arrProc[-OP_NE_DELEGATES] = new Oper(OperNeDelegates);
			arrProc[-OP_ADDRESS_OF] = new Oper(OperNop);

			arrProc[-OP_CREATE_INDEX_OBJECT] = new Oper(OperCreateIndexObject);
			arrProc[-OP_ADD_INDEX] = new Oper(OperAddIndex);
			arrProc[-OP_SETUP_INDEX_OBJECT] = new Oper(OperSetupIndexObject);

			arrProc[-OP_CREATE_TYPE_REFERENCE] = new Oper(OperNop);
			arrProc[-OP_CREATE_FIELD] = new Oper(OperCreateField);
			arrProc[-OP_CREATE_PROPERTY] = new Oper(OperCreateProperty);
			arrProc[-OP_CREATE_EVENT] = new Oper(OperCreateEvent);
			arrProc[-OP_CREATE_NAMESPACE] = new Oper(OperCreateNamespace);
			arrProc[-OP_BEGIN_USING] = new Oper(OperNop);
			arrProc[-OP_END_USING] = new Oper(OperNop);
			arrProc[-OP_ADD_MODIFIER] = new Oper(OperAddModifier);
			arrProc[-OP_ADD_ANCESTOR] = new Oper(OperNop);
			arrProc[-OP_ADD_UNDERLYING_TYPE] = new Oper(OperNop);
			arrProc[-OP_ADD_READ_ACCESSOR] = new Oper(OperAddReadAccessor);
			arrProc[-OP_ADD_WRITE_ACCESSOR] = new Oper(OperAddWriteAccessor);
			arrProc[-OP_SET_DEFAULT] = new Oper(OperSetDefault);
			arrProc[-OP_ADD_ADD_ACCESSOR] = new Oper(OperAddAddAccessor);
			arrProc[-OP_ADD_REMOVE_ACCESSOR] = new Oper(OperAddRemoveAccessor);
			arrProc[-OP_ADD_PATTERN] = new Oper(OperAddPattern);
			arrProc[-OP_ADD_IMPLEMENTS] = new Oper(OperNop);
			arrProc[-OP_ADD_HANDLES] = new Oper(OperNop);

			arrProc[-OP_ADD_MIN_VALUE] = new Oper(OperNop);
			arrProc[-OP_ADD_MAX_VALUE] = new Oper(OperNop);
			arrProc[-OP_ADD_ARRAY_RANGE] = new Oper(OperNop);
			arrProc[-OP_ADD_ARRAY_INDEX] = new Oper(OperNop);

			arrProc[-OP_BEGIN_CALL] = new Oper(OperNop);

			arrProc[-OP_ASSIGN_TYPE] = new Oper(OperNop);
			arrProc[-OP_ASSIGN_COND_TYPE] = new Oper(OperNop);
			arrProc[-OP_CREATE_USING_ALIAS] = new Oper(OperCreateUsingAlias);
			arrProc[-OP_CREATE_REF_TYPE] = new Oper(OperNop);
			arrProc[-OP_SET_REF_TYPE] = new Oper(OperNop);

			arrProc[-OP_EVAL] = new Oper(OperNop);
			arrProc[-OP_EVAL_TYPE] = new Oper(OperNop);
			arrProc[-OP_EVAL_BASE_TYPE] = new Oper(OperNop);
			arrProc[-OP_ASSIGN_NAME] = new Oper(OperNop);
			arrProc[-OP_NOP] = new Oper(OperNop);
			arrProc[-OP_INIT_STATIC_VAR] = new Oper(OperNop);
			arrProc[-OP_LABEL] = new Oper(OperNop);

			arrProc[-OP_ASSIGN] = new Oper(OperAssign);
			arrProc[-OP_ASSIGN_STRUCT] = new Oper(OperAssignStruct);

			arrProc[-OP_PLUS] = new Oper(OperPlus);
			arrProc[-OP_INC] = new Oper(OperPlus);
			arrProc[-OP_MINUS] = new Oper(OperMinus);
			arrProc[-OP_DEC] = new Oper(OperMinus);
			arrProc[-OP_MULT] = new Oper(OperMult);
			arrProc[-OP_EXPONENT] = new Oper(OperExp);
			arrProc[-OP_DIV] = new Oper(OperDiv);

			arrProc[-OP_EQ] = new Oper(OperEq);
			arrProc[-OP_NE] = new Oper(OperNe);
			arrProc[-OP_GT] = new Oper(OperGt);
			arrProc[-OP_GE] = new Oper(OperGe);
			arrProc[-OP_LT] = new Oper(OperLt);
			arrProc[-OP_LE] = new Oper(OperLe);
			arrProc[-OP_IS] = new Oper(OperIs);
			arrProc[-OP_AS] = new Oper(OperAs);

			arrProc[-OP_UPCASE_ON] = new Oper(OperNop);
			arrProc[-OP_UPCASE_OFF] = new Oper(OperNop);
			arrProc[-OP_EXPLICIT_ON] = new Oper(OperNop);
			arrProc[-OP_EXPLICIT_OFF] = new Oper(OperNop);
			arrProc[-OP_STRICT_ON] = new Oper(OperNop);
			arrProc[-OP_STRICT_OFF] = new Oper(OperNop);
			arrProc[-OP_HALT] = new Oper(OperHalt);
			arrProc[-OP_PRINT] = new Oper(OperPrint);

			arrProc[-OP_GO] = new Oper(OperGo);
			arrProc[-OP_GO_FALSE] = new Oper(OperGoFalse);
			arrProc[-OP_GO_TRUE] = new Oper(OperGoTrue);
			arrProc[-OP_GO_NULL] = new Oper(OperGoNull);
			arrProc[-OP_GOTO_START] = new Oper(OperGotoStart);
			arrProc[-OP_GOTO_CONTINUE] = new Oper(OperGotoContinue);

			arrProc[-OP_CREATE_METHOD] = new Oper(OperCreateMethod);
			arrProc[-OP_ADD_EXPLICIT_INTERFACE] = new Oper(OperAddExplicitInterface);
			arrProc[-OP_ADD_PARAM] = new Oper(OperAddParam);
			arrProc[-OP_ADD_PARAMS] = new Oper(OperAddParams);
			arrProc[-OP_ADD_DEFAULT_VALUE] = new Oper(OperAddDefaultValue);
			arrProc[-OP_INIT_METHOD] = new Oper(OperInitMethod);
			arrProc[-OP_DECLARE_LOCAL_VARIABLE] = new Oper(OperDeclareLocalVariable);
			arrProc[-OP_DECLARE_LOCAL_VARIABLE_RUNTIME] = new Oper(OperNop);
			arrProc[-OP_DECLARE_LOCAL_SIMPLE] = new Oper(OperNop);
			arrProc[-OP_END_METHOD] = new Oper(OperEndMethod);

			arrProc[-OP_ADD_EVENT_FIELD] = new Oper(OperAddEventField);

			arrProc[-OP_CALL] = new Oper(OperCall);
			arrProc[-OP_CALL_BASE] = new Oper(OperCallBase);
			arrProc[-OP_CALL_SIMPLE] = new Oper(OperCall);
			arrProc[-OP_CALL_VIRT] = new Oper(OperCallVirt);
			arrProc[-OP_DYNAMIC_INVOKE] = new Oper(OperDynamicInvoke);
			arrProc[-OP_CHECK_STRUCT_CONSTRUCTOR] = new Oper(OperNop);
			arrProc[-OP_INSERT_STRUCT_CONSTRUCTORS] = new Oper(OperNop);
			arrProc[-OP_CALL_ADD_EVENT] = new Oper(OperCallAddEvent);
			arrProc[-OP_RAISE_EVENT] = new Oper(OperNop);
			arrProc[-OP_FIND_FIRST_DELEGATE] = new Oper(OperFindFirstDelegate);
			arrProc[-OP_FIND_NEXT_DELEGATE] = new Oper(OperFindNextDelegate);
			arrProc[-OP_PUSH] = new Oper(OperPush);
			arrProc[-OP_RET] = new Oper(OperRet);
			arrProc[-OP_EXIT_SUB] = new Oper(OperExitSub);
			arrProc[-OP_GET_PARAM_VALUE] = new Oper(OperGetParamValue);
            arrProc[-OP_REDIM] = new Oper(OperRedim);

			arrProc[-OP_CAST] = new Oper(OperCast);
			arrProc[-OP_TO_SBYTE] = new Oper(OperToSbyte);
			arrProc[-OP_TO_BYTE] = new Oper(OperToByte);
			arrProc[-OP_TO_USHORT] = new Oper(OperToUshort);
			arrProc[-OP_TO_SHORT] = new Oper(OperToShort);
			arrProc[-OP_TO_UINT] = new Oper(OperToUint);
			arrProc[-OP_TO_INT] = new Oper(OperToInt);
			arrProc[-OP_TO_ULONG] = new Oper(OperToUlong);
			arrProc[-OP_TO_LONG] = new Oper(OperToLong);
			arrProc[-OP_TO_CHAR] = new Oper(OperToChar);
			arrProc[-OP_TO_FLOAT] = new Oper(OperToFloat);
			arrProc[-OP_TO_DOUBLE] = new Oper(OperToDouble);
			arrProc[-OP_TO_DECIMAL] = new Oper(OperToDecimal);
			arrProc[-OP_TO_STRING] = new Oper(OperToString);
			arrProc[-OP_TO_BOOLEAN] = new Oper(OperToBoolean);
			arrProc[-OP_TO_ENUM] = new Oper(OperToEnum);
			arrProc[-OP_TO_CHAR_ARRAY] = new Oper(OperToCharArray);

			arrProc[-OP_TRY_ON] = new Oper(OperTryOn);
			arrProc[-OP_TRY_OFF] = new Oper(OperTryOff);
			arrProc[-OP_THROW] = new Oper(OperThrow);
			arrProc[-OP_CATCH] = new Oper(OperCatch);
			arrProc[-OP_FINALLY] = new Oper(OperFinally);
			arrProc[-OP_DISCARD_ERROR] = new Oper(OperDiscardError);
			arrProc[-OP_EXIT_ON_ERROR] = new Oper(OperExitOnError);
			arrProc[-OP_ONERROR] = new Oper(OperOnError);
			arrProc[-OP_RESUME] = new Oper(OperResume);
			arrProc[-OP_RESUME_NEXT] = new Oper(OperResumeNext);

			arrProc[-OP_SEPARATOR] = new Oper(OperNop);
			arrProc[-OP_DEFINE] = new Oper(OperNop);
			arrProc[-OP_UNDEF] = new Oper(OperNop);
			arrProc[-OP_START_REGION] = new Oper(OperNop);
			arrProc[-OP_END_REGION] = new Oper(OperNop);

		// DETAILED UNARY MINUS OPERATORS

			arrProc[-OP_NEGATION_INT] = new Oper(OperNegationInt);
			arrProc[-OP_NEGATION_LONG] = new Oper(OperNegationLong);
			arrProc[-OP_NEGATION_FLOAT] = new Oper(OperNegationFloat);
			arrProc[-OP_NEGATION_DOUBLE] = new Oper(OperNegationDouble);
			arrProc[-OP_NEGATION_DECIMAL] = new Oper(OperNegationDecimal);

		// DETAILED LOGICAL NEGATION OPERATORS

			arrProc[-OP_LOGICAL_NEGATION_BOOL] = new Oper(OperLogicalNegationBool);

		// DETAILED BITWISE COMPLEMENT OPERATORS

			arrProc[-OP_BITWISE_COMPLEMENT_INT] = new Oper(OperBitwiseComplementInt);
			arrProc[-OP_BITWISE_COMPLEMENT_UINT] = new Oper(OperBitwiseComplementUint);
			arrProc[-OP_BITWISE_COMPLEMENT_LONG] = new Oper(OperBitwiseComplementLong);
			arrProc[-OP_BITWISE_COMPLEMENT_ULONG] = new Oper(OperBitwiseComplementUlong);

		// DETAILED INC OPERATORS

			arrProc[-OP_INC_SBYTE] = new Oper(OperIncSbyte);
			arrProc[-OP_INC_BYTE] = new Oper(OperIncByte);
			arrProc[-OP_INC_SHORT] = new Oper(OperIncShort);
			arrProc[-OP_INC_USHORT] = new Oper(OperIncUshort);
			arrProc[-OP_INC_INT] = new Oper(OperIncInt);
			arrProc[-OP_INC_UINT] = new Oper(OperIncUint);
			arrProc[-OP_INC_LONG] = new Oper(OperIncLong);
			arrProc[-OP_INC_ULONG] = new Oper(OperIncUlong);
			arrProc[-OP_INC_CHAR] = new Oper(OperIncChar);
			arrProc[-OP_INC_FLOAT] = new Oper(OperIncFloat);
			arrProc[-OP_INC_DOUBLE] = new Oper(OperIncDouble);
			arrProc[-OP_INC_DECIMAL] = new Oper(OperIncDecimal);

		// DETAILED DEC OPERATORS

			arrProc[-OP_DEC_SBYTE] = new Oper(OperDecSbyte);
			arrProc[-OP_DEC_BYTE] = new Oper(OperDecByte);
			arrProc[-OP_DEC_SHORT] = new Oper(OperDecShort);
			arrProc[-OP_DEC_USHORT] = new Oper(OperDecUshort);
			arrProc[-OP_DEC_INT] = new Oper(OperDecInt);
			arrProc[-OP_DEC_UINT] = new Oper(OperDecUint);
			arrProc[-OP_DEC_LONG] = new Oper(OperDecLong);
			arrProc[-OP_DEC_ULONG] = new Oper(OperDecUlong);
			arrProc[-OP_DEC_CHAR] = new Oper(OperDecChar);
			arrProc[-OP_DEC_FLOAT] = new Oper(OperDecFloat);
			arrProc[-OP_DEC_DOUBLE] = new Oper(OperDecDouble);
			arrProc[-OP_DEC_DECIMAL] = new Oper(OperDecDecimal);

		// DETAILED ADDITION OPERATORS

			arrProc[-OP_ADDITION_INT] = new Oper(OperAdditionInt);
			arrProc[-OP_ADDITION_UINT] = new Oper(OperAdditionUint);
			arrProc[-OP_ADDITION_LONG] = new Oper(OperAdditionLong);
			arrProc[-OP_ADDITION_ULONG] = new Oper(OperAdditionUlong);
			arrProc[-OP_ADDITION_FLOAT] = new Oper(OperAdditionFloat);
			arrProc[-OP_ADDITION_DOUBLE] = new Oper(OperAdditionDouble);
			arrProc[-OP_ADDITION_DECIMAL] = new Oper(OperAdditionDecimal);
			arrProc[-OP_ADDITION_STRING] = new Oper(OperAdditionString);

		// DETAILED SUBTRACTION OPERATORS

			arrProc[-OP_SUBTRACTION_INT] = new Oper(OperSubtractionInt);
			arrProc[-OP_SUBTRACTION_UINT] = new Oper(OperSubtractionUint);
			arrProc[-OP_SUBTRACTION_LONG] = new Oper(OperSubtractionLong);
			arrProc[-OP_SUBTRACTION_ULONG] = new Oper(OperSubtractionUlong);
			arrProc[-OP_SUBTRACTION_FLOAT] = new Oper(OperSubtractionFloat);
			arrProc[-OP_SUBTRACTION_DOUBLE] = new Oper(OperSubtractionDouble);
			arrProc[-OP_SUBTRACTION_DECIMAL] = new Oper(OperSubtractionDecimal);

		// DETAILED MULTIPLICATION OPERATORS

			arrProc[-OP_MULTIPLICATION_INT] = new Oper(OperMultiplicationInt);
			arrProc[-OP_MULTIPLICATION_UINT] = new Oper(OperMultiplicationUint);
			arrProc[-OP_MULTIPLICATION_LONG] = new Oper(OperMultiplicationLong);
			arrProc[-OP_MULTIPLICATION_ULONG] = new Oper(OperMultiplicationUlong);
			arrProc[-OP_MULTIPLICATION_FLOAT] = new Oper(OperMultiplicationFloat);
			arrProc[-OP_MULTIPLICATION_DOUBLE] = new Oper(OperMultiplicationDouble);
			arrProc[-OP_MULTIPLICATION_DECIMAL] = new Oper(OperMultiplicationDecimal);

		// DETAILED EXPONENT OPERATORS

			arrProc[-OP_EXPONENT_INT] = new Oper(OperExponentInt);
			arrProc[-OP_EXPONENT_UINT] = new Oper(OperExponentUint);
			arrProc[-OP_EXPONENT_LONG] = new Oper(OperExponentLong);
			arrProc[-OP_EXPONENT_ULONG] = new Oper(OperExponentUlong);
			arrProc[-OP_EXPONENT_FLOAT] = new Oper(OperExponentFloat);
			arrProc[-OP_EXPONENT_DOUBLE] = new Oper(OperExponentDouble);
			arrProc[-OP_EXPONENT_DECIMAL] = new Oper(OperExponentDecimal);

		// DETAILED DIVISION OPERATORS

			arrProc[-OP_DIVISION_INT] = new Oper(OperDivisionInt);
			arrProc[-OP_DIVISION_UINT] = new Oper(OperDivisionUint);
			arrProc[-OP_DIVISION_LONG] = new Oper(OperDivisionLong);
			arrProc[-OP_DIVISION_ULONG] = new Oper(OperDivisionUlong);
			arrProc[-OP_DIVISION_FLOAT] = new Oper(OperDivisionFloat);
			arrProc[-OP_DIVISION_DOUBLE] = new Oper(OperDivisionDouble);
			arrProc[-OP_DIVISION_DECIMAL] = new Oper(OperDivisionDecimal);

		// DETAILED REMAINDER OPERATORS

			arrProc[-OP_REMAINDER_INT] = new Oper(OperRemainderInt);
			arrProc[-OP_REMAINDER_UINT] = new Oper(OperRemainderUint);
			arrProc[-OP_REMAINDER_LONG] = new Oper(OperRemainderLong);
			arrProc[-OP_REMAINDER_ULONG] = new Oper(OperRemainderUlong);
			arrProc[-OP_REMAINDER_FLOAT] = new Oper(OperRemainderFloat);
			arrProc[-OP_REMAINDER_DOUBLE] = new Oper(OperRemainderDouble);
			arrProc[-OP_REMAINDER_DECIMAL] = new Oper(OperRemainderDecimal);

		// DETAILED LEFT SHIFT OPERATORS

			arrProc[-OP_LEFT_SHIFT_INT] = new Oper(OperLeftShiftInt);
			arrProc[-OP_LEFT_SHIFT_UINT] = new Oper(OperLeftShiftUint);
			arrProc[-OP_LEFT_SHIFT_LONG] = new Oper(OperLeftShiftLong);
			arrProc[-OP_LEFT_SHIFT_ULONG] = new Oper(OperLeftShiftUlong);

		// DETAILED RIGHT SHIFT OPERATORS

			arrProc[-OP_RIGHT_SHIFT_INT] = new Oper(OperRightShiftInt);
			arrProc[-OP_RIGHT_SHIFT_UINT] = new Oper(OperRightShiftUint);
			arrProc[-OP_RIGHT_SHIFT_LONG] = new Oper(OperRightShiftLong);
			arrProc[-OP_RIGHT_SHIFT_ULONG] = new Oper(OperRightShiftUlong);

		// DETAILED BITWISE AND OPERATORS

			arrProc[-OP_BITWISE_AND_INT] = new Oper(OperBitwiseAndInt);
			arrProc[-OP_BITWISE_AND_UINT] = new Oper(OperBitwiseAndUint);
			arrProc[-OP_BITWISE_AND_LONG] = new Oper(OperBitwiseAndLong);
			arrProc[-OP_BITWISE_AND_ULONG] = new Oper(OperBitwiseAndUlong);
			arrProc[-OP_BITWISE_AND_BOOL] = new Oper(OperBitwiseAndBool);

		// DETAILED BITWISE OR OPERATORS

			arrProc[-OP_BITWISE_OR_INT] = new Oper(OperBitwiseOrInt);
			arrProc[-OP_BITWISE_OR_UINT] = new Oper(OperBitwiseOrUint);
			arrProc[-OP_BITWISE_OR_LONG] = new Oper(OperBitwiseOrLong);
			arrProc[-OP_BITWISE_OR_ULONG] = new Oper(OperBitwiseOrUlong);
			arrProc[-OP_BITWISE_OR_BOOL] = new Oper(OperBitwiseOrBool);

		// DETAILED BITWISE XOR OPERATORS

			arrProc[-OP_BITWISE_XOR_INT] = new Oper(OperBitwiseXorInt);
			arrProc[-OP_BITWISE_XOR_UINT] = new Oper(OperBitwiseXorUint);
			arrProc[-OP_BITWISE_XOR_LONG] = new Oper(OperBitwiseXorLong);
			arrProc[-OP_BITWISE_XOR_ULONG] = new Oper(OperBitwiseXorUlong);
			arrProc[-OP_BITWISE_XOR_BOOL] = new Oper(OperBitwiseXorBool);

		// DETAILED LT OPERATORS

			arrProc[-OP_LT_INT] = new Oper(OperLessThanInt);
			arrProc[-OP_LT_UINT] = new Oper(OperLessThanUint);
			arrProc[-OP_LT_LONG] = new Oper(OperLessThanLong);
			arrProc[-OP_LT_ULONG] = new Oper(OperLessThanUlong);
			arrProc[-OP_LT_FLOAT] = new Oper(OperLessThanFloat);
			arrProc[-OP_LT_DOUBLE] = new Oper(OperLessThanDouble);
			arrProc[-OP_LT_DECIMAL] = new Oper(OperLessThanDecimal);
			arrProc[-OP_LT_STRING] = new Oper(OperLessThanString);

		// DETAILED LE OPERATORS

			arrProc[-OP_LE_INT] = new Oper(OperLessThanOrEqualInt);
			arrProc[-OP_LE_UINT] = new Oper(OperLessThanOrEqualUint);
			arrProc[-OP_LE_LONG] = new Oper(OperLessThanOrEqualLong);
			arrProc[-OP_LE_ULONG] = new Oper(OperLessThanOrEqualUlong);
			arrProc[-OP_LE_FLOAT] = new Oper(OperLessThanOrEqualFloat);
			arrProc[-OP_LE_DOUBLE] = new Oper(OperLessThanOrEqualDouble);
			arrProc[-OP_LE_DECIMAL] = new Oper(OperLessThanOrEqualDecimal);
			arrProc[-OP_LE_STRING] = new Oper(OperLessThanOrEqualString);

		// DETAILED GT OPERATORS

			arrProc[-OP_GT_INT] = new Oper(OperGreaterThanInt);
			arrProc[-OP_GT_UINT] = new Oper(OperGreaterThanUint);
			arrProc[-OP_GT_LONG] = new Oper(OperGreaterThanLong);
			arrProc[-OP_GT_ULONG] = new Oper(OperGreaterThanUlong);
			arrProc[-OP_GT_FLOAT] = new Oper(OperGreaterThanFloat);
			arrProc[-OP_GT_DOUBLE] = new Oper(OperGreaterThanDouble);
			arrProc[-OP_GT_DECIMAL] = new Oper(OperGreaterThanDecimal);
			arrProc[-OP_GT_STRING] = new Oper(OperGreaterThanString);

		// DETAILED GE OPERATORS

			arrProc[-OP_GE_INT] = new Oper(OperGreaterThanOrEqualInt);
			arrProc[-OP_GE_UINT] = new Oper(OperGreaterThanOrEqualUint);
			arrProc[-OP_GE_LONG] = new Oper(OperGreaterThanOrEqualLong);
			arrProc[-OP_GE_ULONG] = new Oper(OperGreaterThanOrEqualUlong);
			arrProc[-OP_GE_FLOAT] = new Oper(OperGreaterThanOrEqualFloat);
			arrProc[-OP_GE_DOUBLE] = new Oper(OperGreaterThanOrEqualDouble);
			arrProc[-OP_GE_DECIMAL] = new Oper(OperGreaterThanOrEqualDecimal);
			arrProc[-OP_GE_STRING] = new Oper(OperGreaterThanOrEqualString);

		// DETAILED EQ OPERATORS

			arrProc[-OP_EQ_INT] = new Oper(OperEqualityInt);
			arrProc[-OP_EQ_UINT] = new Oper(OperEqualityUint);
			arrProc[-OP_EQ_LONG] = new Oper(OperEqualityLong);
			arrProc[-OP_EQ_ULONG] = new Oper(OperEqualityUlong);
			arrProc[-OP_EQ_FLOAT] = new Oper(OperEqualityFloat);
			arrProc[-OP_EQ_DOUBLE] = new Oper(OperEqualityDouble);
			arrProc[-OP_EQ_DECIMAL] = new Oper(OperEqualityDecimal);
			arrProc[-OP_EQ_STRING] = new Oper(OperEqualityString);
			arrProc[-OP_EQ_BOOL] = new Oper(OperEqualityBool);
			arrProc[-OP_EQ_OBJECT] = new Oper(OperEqualityObject);

		// DETAILED NE OPERATORS

			arrProc[-OP_NE_INT] = new Oper(OperInequalityInt);
			arrProc[-OP_NE_UINT] = new Oper(OperInequalityUint);
			arrProc[-OP_NE_LONG] = new Oper(OperInequalityLong);
			arrProc[-OP_NE_ULONG] = new Oper(OperInequalityUlong);
			arrProc[-OP_NE_FLOAT] = new Oper(OperInequalityFloat);
			arrProc[-OP_NE_DOUBLE] = new Oper(OperInequalityDouble);
			arrProc[-OP_NE_DECIMAL] = new Oper(OperInequalityDecimal);
			arrProc[-OP_NE_STRING] = new Oper(OperInequalityString);
			arrProc[-OP_NE_BOOL] = new Oper(OperInequalityBool);
			arrProc[-OP_NE_OBJECT] = new Oper(OperInequalityObject);

			arrProc[-OP_SWAPPED_ARGUMENTS] = new Oper(OperSwappedArguments);

			prog = new PaxArrayList();
			for (i = 0; i < FIRST_PROG_CARD; i++)
				prog.Add(new ProgRec());

			n = 0;
			card = 0;
		}


		/// <summary>
		/// Resets run-time stage.
		/// </summary>
		public void Reset()
		{
			while (Card > 0)
			{
				this[Card].Reset();
				Card --;
			}
			card = 0;
			ResetRunStageStructs();
			breakpoint_list.Clear();
			get_item_list.Clear();
		}

		/// <summary>
		/// Resets run-time stage structures.
		/// </summary>
		public void ResetRunStageStructs()
		{
			n = 0;
			stack.Clear();
			state_stack.Clear();
			try_stack.Clear();
			checked_stack.Clear();
			custom_ex_list.Clear();
			callstack.Clear();
			resume_stack.Clear();

			for (int i=1; i <= card; i++)
			{
				if (this[i].op == OP_INIT_STATIC_VAR)
				{
                	this[i].res = 0;
                }
            }

		}

		/// <summary>
		/// Returns next instruction of p-code which is not separator.
		/// </summary>
		ProgRec NextInstruction(int j)
		{
			j++;
			while (this[j].op == OP_SEPARATOR)
				j++;
			return this[j];
		}

		/// <summary>
		/// Creates new label.
		/// </summary>
		public int AppLabel()
		{
			return symbol_table.AppLabel();
		}

		/// <summary>
		/// Assigns value of label with last p-code instruction.
		/// </summary>
		public void SetLabelHere(int label_id)
		{
			symbol_table.SetLabel(label_id, Card + 1);
		}

		/// <summary>
		/// Creates new variable.
		/// </summary>
		int AppVar(int level)
		{
			int result = symbol_table.AppVar();
			symbol_table[result].Level = level;
			return result;
		}

		/// <summary>
		/// Creates new variable.
		/// </summary>
		int AppVar(int level, int type_id)
		{
			int result = symbol_table.AppVar();
			symbol_table[result].Level = level;
			symbol_table[result].TypeId = type_id;
			return result;
		}

		/// <summary>
		/// Inserts 'number' of OP_NOP operators at position 'pos' of p-code.
		/// </summary>
		void InsertOperators(int pos, int number)
		{
			for (int i = 0; i < number; i++)
			{
				ProgRec rec = new ProgRec();
				rec.op = OP_NOP;
				prog.Insert(pos, rec);
			}
			Card += number;
		}

		/// <summary>
		/// Returns id of type of the given id.
		/// </summary>
		public int GetTypeId(int id)
		{
			return symbol_table[id].TypeId;
		}

		/// <summary>
		/// Assigns type id to the given id.
		/// </summary>
		public void SetTypeId(int id, int type_id)
		{
			symbol_table[id].TypeId = type_id;
		}

		/// <summary>
		/// Assigns value to the given id.
		/// </summary>
		public void PutValue(int id, object value)
		{
			symbol_table[id].Value = value;
		}

		/// <summary>
		/// Returns value of the given id.
		/// </summary>
		public object GetValue(int id)
		{
			return symbol_table[id].Value;
		}

		/// <summary>
		/// Returns value of the given id as System.Boolean.
		/// </summary>
		public bool GetValueAsBool(int id)
		{
			return symbol_table[id].ValueAsBool;
		}

		/// <summary>
		/// Assigns value of the given id.
		/// </summary>
		public void PutValueAsBool(int id, bool value)
		{
			symbol_table[id].ValueAsBool = value;
		}

		/// <summary>
		/// Returns value of the given id as System.Byte.
		/// </summary>
		public byte GetValueAsByte(int id)
		{
			return symbol_table[id].ValueAsByte;
		}

		/// <summary>
		/// Returns value of the given id as System.Char.
		/// </summary>
		public char GetValueAsChar(int id)
		{
			return symbol_table[id].ValueAsChar;
		}

		/// <summary>
		/// Returns value of the given id as System.Decimal.
		/// </summary>
		public decimal GetValueAsDecimal(int id)
		{
			return symbol_table[id].ValueAsDecimal;
		}

		/// <summary>
		/// Assigns value of the given id.
		/// </summary>
		public void PutValueAsDecimal(int id, decimal value)
		{
			symbol_table[id].ValueAsDecimal = value;
		}

		/// <summary>
		/// Returns value of the given id as System.Double.
		/// </summary>
		public double GetValueAsDouble(int id)
		{
			return symbol_table[id].ValueAsDouble;
		}

		/// <summary>
		/// Assigns value of the given id.
		/// </summary>
		public void PutValueAsDouble(int id, double value)
		{
			symbol_table[id].ValueAsDouble = value;
		}

		/// <summary>
		/// Returns value of the given id as System.Float.
		/// </summary>
		public float GetValueAsFloat(int id)
		{
			return symbol_table[id].ValueAsFloat;
		}

		/// <summary>
		/// Assigns value of the given id.
		/// </summary>
		public void PutValueAsFloat(int id, float value)
		{
			symbol_table[id].ValueAsFloat = value;
		}

		/// <summary>
		/// Returns value of the given id as System.Int32.
		/// </summary>
		public int GetValueAsInt(int id)
		{
			return symbol_table[id].ValueAsInt;
		}

		/// <summary>
		/// Assigns value of the given id.
		/// </summary>
		public void PutValueAsInt(int id, int value)
		{
			symbol_table[id].ValueAsInt = value;
		}

		/// <summary>
		/// Returns value of the given id as System.Int64.
		/// </summary>
		public long GetValueAsLong(int id)
		{
			return symbol_table[id].ValueAsLong;
		}

		/// <summary>
		/// Assigns value of the given id.
		/// </summary>
		public void PutValueAsLong(int id, long value)
		{
			symbol_table[id].ValueAsLong = value;
		}

		/// <summary>
		/// Returns value of the given id as System.Int16.
		/// </summary>
		public short GetValueAsShort(int id)
		{
			return symbol_table[id].ValueAsShort;
		}

		/// <summary>
		/// Returns value of the given id as System.String.
		/// </summary>
		public string GetValueAsString(int id)
		{
			return symbol_table[id].ValueAsString;
		}

		/// <summary>
		/// Assigns value of the given id.
		/// </summary>
		public void PutValueAsString(int id, string value)
		{
			symbol_table[id].ValueAsString = value;
		}

		/// <summary>
		/// Returns value of the given id as System.Object.
		/// </summary>
		public ClassObject GetClassObject(int id)
		{
			return scripter.GetClassObject(id);
		}

		/// <summary>
		/// Returns value of the given id as ClassObject.
		/// </summary>
		public ClassObject GetClassObjectEx(int id)
		{
			return scripter.GetClassObjectEx(id);
		}

		/// <summary>
		/// Returns value of the given id as MemberObject.
		/// </summary>
		public MemberObject GetMemberObject(int id)
		{
			return scripter.GetMemberObject(id);
		}

		/// <summary>
		/// Returns value of the given id as IndexObject.
		/// </summary>
		public IndexObject GetIndexObject(int id)
		{
			return scripter.GetIndexObject(id);
		}

		/// <summary>
		/// Returns value of the given id as PropertyObject.
		/// </summary>
		public PropertyObject GetPropertyObject(int id)
		{
			return scripter.GetPropertyObject(id);
		}

		/// <summary>
		/// Returns value of the given id as EventObject.
		/// </summary>
		public EventObject GetEventObject(int id)
		{
			return scripter.GetEventObject(id);
		}

		/// <summary>
		/// Returns value of the given id as ObjectObject.
		/// </summary>
		public ObjectObject GetObjectObject(int id)
		{
			return scripter.ToScriptObject(GetValue(id));
		}

		/// <summary>
		/// Pops value from stack as ObjectObject.
		/// </summary>
		public ObjectObject PopObjectObject()
		{
			return scripter.ToScriptObject(stack.Pop());
		}

		/// <summary>
		/// Returns value of the given id as FunctionObject.
		/// </summary>
		public FunctionObject GetFunctionObject(int id)
		{
			return (FunctionObject) GetVal(id);
		}

		/// <summary>
		/// Returns value of the given id as FieldObject.
		/// </summary>
		public FieldObject GetFieldObject(int id)
		{
			return (FieldObject) GetVal(id);
		}

		/// <summary>
		/// Pops value from stack as FunctionObject.
		/// </summary>
		public FunctionObject PopFunctionObject()
		{
			return (FunctionObject) stack.Pop();
		}

		/// <summary>
		/// Assigns value to id.
		/// </summary>
		public void PutVal(int id, object value)
		{
			symbol_table[id].Val = value;
		}

		/// <summary>
		/// Returns value of id.
		/// </summary>
		public object GetVal(int id)
		{
			return symbol_table[id].Val;
		}

		/// <summary>
		/// Returns current p-code instruction.
		/// </summary>
		public ProgRec GetCurrentIstruction()
		{
			return this[n];
		}

		/// <summary>
		/// Saves p-code state.
		/// </summary>
		public void SaveState()
		{
			state_stack.Push(n);
			state_stack.Push(Card);
			SaveCheckedState();
		}

		/// <summary>
		/// Restores p-code state.
		/// </summary>
		public void RestoreState()
		{
			Card = (int) state_stack.Pop();
			n = (int) state_stack.Pop();
			RestoreCheckedState();
		}

		/// <summary>
		/// Returns operator name.
		/// </summary>
		public string GetOperName(int op, int l)
		{
			return PaxSystem.Norm(Operators[-op], l);
		}

		/// <summary>
		/// Assigns p-code instruction.
		/// </summary>
		public void SetInstruction(int line, int op, int arg1, int arg2, int res)
		{
			ProgRec progrec = (ProgRec) prog[line];
			progrec.op = op;
			progrec.arg1 = arg1;
			progrec.arg2 = arg2;
			progrec.res = res;

			if (op == OP_CREATE_REFERENCE || op == OP_CREATE_TYPE_REFERENCE)
			{
				symbol_table[res].Level = arg1;
				symbol_table[res].Kind = MemberKind.Ref;
			}
			else if (op == OP_CREATE_INDEX_OBJECT)
			{
				symbol_table[res].Level = arg1;
				symbol_table[res].Kind = MemberKind.Index;
			}
		}

		public void SetUpcase(int line, Upcase value)
		{
			ProgRec progrec = (ProgRec) prog[line];
			progrec.upcase = value;
		}

		/// <summary>
		/// Assigns new p-code instruction.
		/// </summary>
		int AddInstruction(int op, int arg1, int arg2, int res)
		{
			Card ++;
			SetInstruction(Card, op, arg1, arg2, res);
			return Card;
		}

		public void RemoveNOP()
		{
			for (int i = card; i > 0; i--)
				if (this[i].op == OP_NOP)
				{
					prog.RemoveAt(i);
					card--;
				}
			for (int i = 1; i <= card; i++)
			{
				this[i].FinalNumber = i;
			}
		}

		/// <summary>
		/// Preliminary optimization.
		/// </summary>
		public void Optimization()
		{
			for (int i = 1; i < card; i++)
			{
				int op = this[i].op;

				if (op == OP_INC_INT || op == OP_INC_LONG ||
					op == OP_DEC_INT || op == OP_DEC_LONG)
				{
					int k = i - 1;
					while (this[k].op == OP_NOP)
						--k;

					if (this[k].op == OP_ASSIGN && this[i].arg1 == this[k].arg2)
					{
						int z = this[k].res;
						bool used = false;
						for (int j = i + 1; j <= Card; j++)
						{
							if (this[j].arg1 == z || this[j].arg2 == z ||
								this[j].res == z)
							{
								used = true;
								break;
							}
						}
						if (!used)
							this[k].op = OP_NOP;
					}
				}

				if (this[i+1].op == OP_ASSIGN && this[i].res == this[i+1].arg2)
				{
					bool ok = false;
					for (int j = 0; j < detailed_operators.Count; j++)
						if (detailed_operators.Items2[j] == op)
						{
							ok = true;
							break;
						}
					if (ok)
					{
						this[i].res = this[i+1].res;

						this[i+1].op = OP_NOP;
						this[i+1].arg1 = 0;
						this[i+1].arg2 = 0;
						this[i+1].res = 0;
					}
				}
			}

			RemoveNOP();
			symbol_table.SetupFastAccessRecords();
		}

		/// <summary>
		/// Assigns label values to p-code instructions.
		/// </summary>
		public void LinkGoTo()
		{
			LinkGoToEx(0);
		}

		/// <summary>
		/// Assigns label values to p-code instructions.
		/// </summary>
		public void LinkGoToEx(int init_n)
		{
			int i;
			ProgRec r;
			for (i = init_n + 1; i <= Card; i++)
			{
				r = (ProgRec) prog[i];
				if ((r.op == OP_GO) ||
					(r.op == OP_GOTO_START) ||
					(r.op == OP_GO_FALSE) ||
					(r.op == OP_GO_TRUE) ||
					(r.op == OP_GO_NULL))
				{
					object v = GetVal(r.arg1);
					if (v == null)
					{
						string s = symbol_table[r.arg1].Name;
						int l = symbol_table[r.arg1].Level;
						int j = symbol_table.LookupID(s, l, true);
						if (j == 0)
							scripter.CreateErrorObjectEx(Errors.CS0159, s);
						v = GetVal(j);
						if (v == null)
							scripter.CreateErrorObjectEx(Errors.CS0159, s);
					}

					ProgRec z = symbol_table[r.arg1].CodeProgRec;
					r.arg1 = z.FinalNumber;

//					r.arg1 = (int) v;
				}
			}
		}

		/// <summary>
		/// Creates all script-defined types.
		/// </summary>
		public void CreateClassObjects()
		{
			for (int i=1; i <= Card; i++)
			{
				r = (ProgRec) prog[i];
				int op = r.op;
				n = i;

				if (op == OP_CREATE_NAMESPACE)
				{
					OperCreateNamespace();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_CREATE_USING_ALIAS)
				{
					OperCreateUsingAlias();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_CREATE_CLASS)
				{
					OperCreateClass();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_CREATE_FIELD)
				{
					OperCreateField();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_CREATE_PROPERTY)
				{
					OperCreateProperty();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_CREATE_EVENT)
				{
					OperCreateEvent();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_ADD_EVENT_FIELD)
					OperAddEventField();
				else if (op == OP_ADD_READ_ACCESSOR)
					OperAddReadAccessor();
				else if (op == OP_ADD_WRITE_ACCESSOR)
					OperAddWriteAccessor();
				else if (op == OP_SET_DEFAULT)
					OperSetDefault();
				else if (op == OP_ADD_ADD_ACCESSOR)
					OperAddAddAccessor();
				else if (op == OP_ADD_REMOVE_ACCESSOR)
					OperAddRemoveAccessor();
				else if (op == OP_ADD_MIN_VALUE)
					OperAddMinValue();
				else if (op == OP_ADD_MAX_VALUE)
					OperAddMaxValue();

				else if (op == OP_CREATE_METHOD)
					OperCreateMethod();
				else if (op == OP_ADD_PATTERN)
					OperAddPattern();
				else if (op == OP_ADD_PARAM)
					OperAddParam();
				else if (op == OP_ADD_PARAMS)
					OperAddParams();
				else if (op == OP_ADD_DEFAULT_VALUE)
					OperAddDefaultValue();
				else if (op == OP_INIT_METHOD)
					OperInitMethod();
				else if (op == OP_END_METHOD)
					OperEndMethod();
				else if (op == OP_ADD_MODIFIER)
					OperAddModifier();

			}
		}

		/// <summary>
		/// Replaces old id with new id in p-code.
		/// </summary>
		public void ReplaceId(int old_id, int new_id)
		{
			if (old_id == new_id)
				return;

			for (int i=1; i <= Card; i++)
			{
				ProgRec r = (ProgRec) prog[i];
				int op = r.op;

				if (op != OP_SEPARATOR)
				{
					if (r.arg1 == old_id)
						r.arg1 = new_id;

					if (r.arg2 == old_id)
						r.arg2 = new_id;

					if (r.res == old_id)
						r.res = new_id;
				}
			}

			for (int i=old_id; i <= symbol_table.Card; i++)
			{
				if (symbol_table[i].Kind == MemberKind.Ref)
					if (symbol_table[i].Level == old_id)
					{
						symbol_table[i].Level = new_id;
					}
			}
		}

		public void ReplaceIdEx(int old_id, int new_id, int start_pos, bool local)
		{
			if (old_id == new_id)
				return;

			MemberKind k = symbol_table[new_id].Kind;	

			for (int i=start_pos; i <= Card; i++)
			{
				ProgRec r = (ProgRec) prog[i];
				int op = r.op;

				if (op == OP_END_METHOD || op == OP_END_MODULE)
				{
					if (k != MemberKind.Type)
						break;
				}

				if (op != OP_SEPARATOR)
				{
					if (r.arg1 == old_id)
						r.arg1 = new_id;

					if (r.arg2 == old_id)
						r.arg2 = new_id;

					if (r.res == old_id)
						r.res = new_id;
				}
			}

			for (int i=old_id; i <= symbol_table.Card; i++)
			{
				if (symbol_table[i].Kind == MemberKind.Ref)
					if (symbol_table[i].Level == old_id)
					{
						symbol_table[i].Level = new_id;
					}
			}
		}

		/// <summary>
		/// Returns available .NET type.
		/// </summary>

		MemberObject FindType(IntegerStack l, string type_name, bool upcase)
		{
			Type t;
			int p;

			string name = PaxSystem.ExtractPrefixName(type_name, out p);

			if (p >= 0)
			{
				if (name == "bool")
					name = "Boolean" + type_name.Substring(p);
				else if (name == "byte")
					name = "Byte" + type_name.Substring(p);
				else if (name == "char")
					name = "Char" + type_name.Substring(p);
				else if (name == "decimal")
					name = "Decimal" + type_name.Substring(p);
				else if (name == "double")
					name = "double" + type_name.Substring(p);
				else if (name == "single")
					name = "Single" + type_name.Substring(p);
				else if (name == "int")
					name = "Int32" + type_name.Substring(p);
				else if (name == "long")
					name = "Int64" + type_name.Substring(p);
				else if (name == "sbyte")
					name = "SByte" + type_name.Substring(p);
				else if (name == "short")
					name = "Int16" + type_name.Substring(p);
				else if (name == "string")
					name = "String" + type_name.Substring(p);
				else if (name == "uint")
					name = "UInt32" + type_name.Substring(p);
				else if (name == "ulong")
					name = "UInt64" + type_name.Substring(p);
				else if (name == "ushort")
					name = "UInt16" + type_name.Substring(p);
				else
					name = type_name;
			}

            string s = name;
            t = Type.GetType(s);
            if (t == null)
                t = scripter.FindAvailableType(s, upcase);

            if (t != null)
            {
                int type_id = symbol_table.RegisterType(t, true);
                return GetMemberObject(type_id);
			}

			for (int j = l.Count - 1; j >= 0; j--)
			{
				int id = l[j];
				s = symbol_table[id].FullName + "." + name;

				t = Type.GetType(s);
				if (t == null)
					t = scripter.FindAvailableType(s, upcase);

				if (t != null)
				{
					int type_id = symbol_table.RegisterType(t, true);
					return GetMemberObject(type_id);
				}
			}

			return null;
		}

		/// <summary>
		/// Returns current script-defined type.
		/// </summary>
		ClassObject GetCurrentClass(IntegerStack l)
		{
			for (int i = l.Count - 1; i >= 0; i--)
			{
				MemberObject m = GetMemberObject(l[i]);
				if (m.Kind == MemberKind.Type)
					return m as ClassObject;
			}
			return null;
		}

		/// <summary>
		/// Removes OP_EVAL from p-code.
		/// </summary>
		public void ProcessEvalOp(IntegerStack l)
		{
			bool upcase = GetUpcase();

			int idx = symbol_table[r.res].NameIndex;
			int eval_id = r.res;
			string ident_name = symbol_table[eval_id].Name;

			MemberObject m = null;
			for (int j=l.Count - 1; j>=0; j--)
			{
				MemberObject mo = GetMemberObject(l[j]);
				m = mo.GetMemberByNameIndex(idx, upcase);
				if (m == null)
					continue;
				if (m.Kind == MemberKind.Constructor && j != l.Count - 1)
					continue;

				int curr_level = l.Peek();
				MemberObject curr_object = GetMemberObject(curr_level);
				if (curr_object.Static)
				{
					if (m.Static)
					{
						r.op = OP_NOP;
						ReplaceIdEx(r.res, m.Id, n, true);
						break;
					}
					else
					{
						if (GetCurrentClass(l).IsOuterMemberId(m.Id))
						{
							// Cannot access a nonstatic member of outer type via nested type
							scripter.CreateErrorObjectEx(Errors.CS0038, mo.Name, GetCurrentClass(l).Name);
							break;
						}

						m = null;
						continue;
					}
				}
				else
				{
					if (m.Static) // ok
					{
						r.op = OP_NOP;
						ReplaceIdEx(r.res, m.Id, n, true);
						break;
					}
					else
					{
						if (GetCurrentClass(l).IsOuterMemberId(m.Id))
						{
							// Cannot access a nonstatic member of outer type via nested type
							scripter.CreateErrorObjectEx(Errors.CS0038, mo.Name, GetCurrentClass(l).Name);
							break;
						}

						if (curr_object.Kind == MemberKind.Type)
						{
							// not implemented
							r.op = OP_NOP;
							break;
						}
						else // method
						{
							FunctionObject f = (FunctionObject) curr_object;

							r.op = OP_CREATE_REFERENCE;
							r.arg1 = f.ThisId;
							symbol_table[r.res].Level = r.arg1;
							symbol_table[r.res].Kind = MemberKind.Ref;
							symbol_table[r.res].TypeId = symbol_table[m.Id].TypeId;

							break;
						}
					}
				}
			}
			
			if (m == null)
			{
				m = FindType(l, ident_name, upcase);
				if (m == null)
				{
					if (symbol_table[eval_id].Kind == MemberKind.Label)
					{
						int lab_id = EvalLabel(eval_id);
						if (lab_id == 0)
							scripter.CreateErrorObjectEx(Errors.UNDECLARED_IDENTIFIER, ident_name);
						else
						{
							r.op = OP_NOP;
							ReplaceIdEx(r.res, lab_id, n, true);
						}
					}
					else
					{
						bool ok = false;
						if (!GetExplicit(n))
						{
							int sub_id = symbol_table[r.res].Level;

							if (this[n + 1].op == OP_ASSIGN &&  this[n + 1].arg1 == r.res)
							{
								r.op = OP_DECLARE_LOCAL_VARIABLE;
								r.arg1 = r.res;
								r.arg2 = sub_id;
								ok = true;
							}
							else
							{
								string s = symbol_table[r.res].Name;
								for (int j = n; j >= 1; j--)
								{
									if (this[j].op == OP_DECLARE_LOCAL_VARIABLE)
									{
										if (this[j].arg2 == sub_id)
										{
											int curr_id = this[j].arg1;
											string curr_s = symbol_table[curr_id].Name;
											if (PaxSystem.CompareStrings(s, curr_s, upcase))
											{
												r.op = OP_NOP;
												ReplaceId(r.res, curr_id);
												ok = true;
												break;
											}
										}
										else
											break;
									}
								}
							}
						}
						if (!ok)
						{
							if (PascalOrBasic(n))
							{
								for (int j = 1; j <= card; j++)
								{
									if (this[j].op == OP_CREATE_CLASS)
									{
										ClassObject c = GetClassObject(this[j].arg1);
										if (c.Static)
										{
											m = c.GetMemberByNameIndex(idx, upcase);
											if (m != null)
											{
												r.op = OP_NOP;
												ReplaceId(r.res, m.Id);
												return;
											}
										}
									}
								}
							}

							if (scripter.DefaultInstanceMethods)
							{
								foreach	(string key in scripter.UserInstances.Keys)
								{
									object instance = scripter.UserInstances[key];
									Type t = instance.GetType();
									MethodInfo[] methods = t.GetMethods();
									foreach (MethodInfo info in methods)
									{
										if (PaxSystem.CompareStrings(ident_name, info.Name, upcase))
										{
											int instance_id = 0;
											for (int j=1; j <= symbol_table.Card; j++)
											{
												if (symbol_table[j].Level == 0 &&
													symbol_table[j].Kind == MemberKind.Var &&
													PaxSystem.CompareStrings(symbol_table[j].Name, key, upcase) &&
													symbol_table[j].Val != null)
													{
														instance_id = j;
														break;
													}
											}
											if (instance_id != 0)
											{
												int type_id = symbol_table.RegisterType(t, false);

												r.op = OP_CREATE_REFERENCE;
												r.arg1 = instance_id;
												symbol_table[r.res].Level = r.arg1;
												symbol_table[r.res].Kind = MemberKind.Ref;
												symbol_table[r.res].TypeId = type_id;
												return;
											}
										}
									}

									PropertyInfo[] properties = t.GetProperties();
									foreach (PropertyInfo info in properties)
									{
										if (PaxSystem.CompareStrings(ident_name, info.Name, upcase))
										{
											int instance_id = 0;
											for (int j=1; j <= symbol_table.Card; j++)
											{
												if (symbol_table[j].Level == 0 &&
													symbol_table[j].Kind == MemberKind.Var &&
													PaxSystem.CompareStrings(symbol_table[j].Name, key, upcase) &&
													symbol_table[j].Val != null)
													{
														instance_id = j;
														break;
													}
											}
											if (instance_id != 0)
											{
												int type_id = symbol_table.RegisterType(t, false);

												r.op = OP_CREATE_REFERENCE;
												r.arg1 = instance_id;
												symbol_table[r.res].Level = r.arg1;
												symbol_table[r.res].Kind = MemberKind.Ref;
												symbol_table[r.res].TypeId = type_id;

												return;
											}
										}
									}

								}
							}

							scripter.CreateErrorObjectEx(Errors.UNDECLARED_IDENTIFIER, ident_name);
						}
					}
				}
				else
				{
					r.op = OP_NOP;
					ReplaceIdEx(r.res, m.Id, n, true);
				}
			}
		}

		int EvalLabel(int l)
		{
			int level = symbol_table[l].Level;
			string name = symbol_table[l].Name;

			for (int i = 1; i <= card; i++)
			{
				if (this[i].op == OP_DECLARE_LOCAL_VARIABLE)
				{
					if (this[i].arg2 == level)
					{
						int id = this[i].arg1;
						MemberKind k = symbol_table[id].Kind;
						if (k == MemberKind.Label)
						{
							string s = symbol_table[id].Name;
							if (name == s)
								return id;
						}
					}
				}
			}
			return 0;
		}

		/// <summary>
		/// Removes OP_EVAL_TYPE from p-code.
		/// </summary>
		public void ProcessEvalType(IntegerStack l)
		{
			bool upcase = GetUpcase();

			int name_index = symbol_table[r.res].NameIndex;
			string type_name = symbol_table[r.res].Name;

			if (scripter.IsStandardType(r.res))
			{
				if (NextInstruction(n).op == OP_CREATE_TYPE_REFERENCE)
				{
					type_name += "." + symbol_table[NextInstruction(n).res].Name;
					// The type or namespace name 'type/namespace' could not be found (are you missing a using directive or an assembly reference?)
					scripter.CreateErrorObjectEx(Errors.CS0246, type_name);
					return;
				}
				r.op = OP_NOP;
				return;
			}

			MemberObject m = null;
			for (int j=l.Count - 1; j>=0; j--)
			{
				MemberObject mo = GetMemberObject(l[j]);
				if (mo.Kind != MemberKind.Type)
					continue;
				if (mo.NameIndex == name_index)
				{
					m = mo;
					break;
				}
				m = mo.GetMemberByNameIndex(name_index, upcase);
				if (m == null)
					continue;
				if (m.Kind == MemberKind.Alias)
				{
					int id = m.Id;
					while (symbol_table[id].Kind == MemberKind.Alias)
					{
						m = GetMemberObject(id);
						id = this[m.PCodeLine].res;
					}
					m = GetMemberObject(id);
					name_index = m.NameIndex;
				}
				if (m.Kind != MemberKind.Type)
					continue;
				if (m.NameIndex == name_index)
					break;

				if (upcase && m != null)
					break;	
			}

			if (m == null)
			{
				int p = PaxSystem.PosCh('[', type_name);

				if (p > 0)
				{
					ClassObject el_type = GetClassObject(r.arg1);
					if (el_type.Imported)
					{
						m = FindType(l, type_name, upcase);
					}
					if (m == null)
					{
						string name = "Object" + type_name.Substring(p);
						m = FindType(l, name, upcase);
						if (m != null)
						{
							int class_id = symbol_table.AppVar();
							symbol_table[class_id].Name = type_name;
							symbol_table[class_id].Kind = MemberKind.Type;
							int owner_id = 0;
							ClassObject c = new ClassObject(scripter, class_id, owner_id, ClassKind.Array);
							c.Imported = true;
							c.ImportedType = (m as ClassObject).ImportedType;
							c.RType = (m as ClassObject).ImportedType;
							PutVal(class_id, c);
							ClassObject o = GetClassObject(owner_id);
							o.AddMember(c);
							m = c;
						}
					}
				}
			}

			if (m == null)
			{
				m = FindType(l, type_name, upcase);
			}

			if (m == null)
			{
				// The type or namespace name 'type/namespace' could not be found (are you missing a using directive or an assembly reference?)
				scripter.CreateErrorObjectEx(Errors.CS0246, type_name);
				return;
			}
			else
			{
				r.op = OP_NOP;
				for (;;)
				{
					n++;
					ProgRec temp_r = (ProgRec) prog[n];
					if (temp_r.op == OP_SEPARATOR)
						continue;
					if (temp_r.op != OP_CREATE_TYPE_REFERENCE)
						break;
					r.op = OP_NOP;
					r = (ProgRec) prog[n];
					MemberObject mo = GetMemberObject(m.Id);
					if (mo.Kind != MemberKind.Type)
					{
						// The type or namespace name 'type/namespace' could not be found (are you missing a using directive or an assembly reference?)
						scripter.CreateErrorObjectEx(Errors.CS0246, type_name);
						break;
					}
					name_index = symbol_table[r.res].NameIndex;
					m = mo.GetMemberByNameIndex(name_index, upcase);
					if (m == null)
					{
						type_name = symbol_table[r.res].FullName;

						if (m == null)
						{
							m = FindType(l, type_name, upcase);
						}
						if (m == null)
						{
							// The type or namespace name 'type/namespace' could not be found (are you missing a using directive or an assembly reference?)
							scripter.CreateErrorObjectEx(Errors.CS0246, type_name);
							break;
						}
					}
				}

				if (m != null)
					scripter.CheckForbiddenType(m.Id);

				r.op = OP_NOP;
				if (!scripter.IsError())
					ReplaceIdEx(r.res, m.Id, n, true);
			}
		}

		/// <summary>
		/// Assigns ancestor types.
		/// </summary>
		void ProcessAddAncestor()
		{
			int class_id = r.arg1;
			int ancestor_id = r.arg2;
			ClassObject c = GetClassObject(class_id);
			ClassObject a = GetClassObject(ancestor_id);

			if (a.Sealed && c.Class_Kind != ClassKind.Subrange)
			{
				scripter.CreateErrorObjectEx(Errors.CS0509, c.Name, a.Name);
				return;
			}

			if (a.IsNamespace)
			{
				scripter.CreateErrorObjectEx(Errors.CS0118, a.Name, "namespace", "class");
				return;
			}

			if (c.AncestorIds.IndexOf(ancestor_id) >= 0)
			{
				scripter.CreateErrorObjectEx(Errors.CS0528, a.Name);
				return;
			}

			if (a.InheritsFrom(c) || (a == c))
			{
				// Circular base class definition between 'class1' and 'class2'
				scripter.CreateErrorObjectEx(Errors.CS0146, c.Name, a.Name);
				return;
			}

			if ((!a.Imported) && (MemberObject.CompareAccessibility(a, c) < 0))
			{
				if (a.IsClass)
					scripter.CreateErrorObjectEx(Errors.CS0060, a.Name, c.Name);
				else if (a.IsInterface)
					scripter.CreateErrorObjectEx(Errors.CS0061, a.Name, c.Name);
			}

			c.AncestorIds.Add(ancestor_id);

			if (a.Imported)
			{
				Type t_base = a.ImportedType.BaseType;

				while (t_base != null && t_base != typeof(object))
				{
					int type_id = scripter.symbol_table.RegisterType(t_base, false);
					a.AncestorIds.Add(type_id);

					t_base = t_base.BaseType;
					a = GetClassObject(type_id);
				}
			}
		}

		/// <summary>
		/// Removes eval instructions from p-code.
		/// </summary>
		public void RemoveEvalOp()
		{
			RemoveEvalOpEx(0, null);
		}

		/// <summary>
		/// Removes eval instructions from p-code.
		/// </summary>
		public void RemoveEvalOpEx(int init_n, IntegerStack init_l)
		{
			if (scripter.IsError()) return;

			bool has_new;

			IntegerStack l;

			if (init_l == null)
				l = new IntegerStack();
			else
				l = init_l.Clone();

			for (int i = init_n + 1; i <= Card; i++)
			{
				r = (ProgRec) prog[i];
				int op = r.op;
				n = i;
				if (op == OP_CREATE_METHOD)
				{
					l.Push(r.arg1);

					int id = r.arg1;
					int owner_id = r.arg2;
					MemberObject m = GetMemberObject(id);

					has_new = m.HasModifier(Modifier.New);

					bool upcase = GetUpcase(n);

					ClassObject c = GetClassObject(owner_id);

					for (int j = 0; j < c.AncestorIds.Count; j++)
					{
						ClassObject a = GetClassObject(c.AncestorIds[j]);

						MemberObject mo = a.GetMemberByNameIndex(m.NameIndex, upcase);
						if (mo != null)
						{
							bool ok = has_new || a.IsInterface ||
								(
								m.HasModifier(Modifier.Override) &&
								mo.HasModifier(Modifier.Virtual) || m.HasModifier(Modifier.Override) || m.HasModifier(Modifier.Shadows));

							if (!ok)
							{
								// The keyword new is required on 'member1' because it hides inherited member 'member2'
								scripter.CreateWarningObjectEx(Errors.CS0108,
									m.FullName, a.FullName + "." + m.Name);
							}
						}
						else
							if (has_new)
								// The member 'member' does not hide an inherited member. The new keyword is not required
								scripter.CreateWarningObjectEx(Errors.CS0109,
									m.FullName);

					}
				}
				else if (op == OP_RET)
					l.Pop();
				else if (op == OP_BEGIN_USING)
				{
					int id = r.arg1;
					while (symbol_table[id].Kind == MemberKind.Alias)
					{
						MemberObject m = GetMemberObject(id);
						id = this[m.PCodeLine].res;
					}

					string s = symbol_table[id].FullName;
					if (scripter.CheckForbiddenNamespace(s))
						scripter.CreateErrorObjectEx(Errors.PAX0006, s);

					l.Push(id);
				}
				else if (op == OP_END_USING)
					l.Pop();
				else if (op == OP_ADD_ANCESTOR)
				{
					ProcessAddAncestor();
					if (scripter.IsError())
						break;
				}
				// check for the new modifier
				else if ((op == OP_CREATE_FIELD) ||
						(op == OP_CREATE_EVENT) ||
						(op == OP_CREATE_PROPERTY))
				{
					int id = r.arg1;
					int owner_id = r.arg2;
					MemberObject m = GetMemberObject(id);
					has_new = m.HasModifier(Modifier.New);

					ClassObject c = GetClassObject(owner_id);

					bool upcase = GetUpcase(n);

					for (int j = 0; j < c.AncestorIds.Count; j++)
					{
						ClassObject a = GetClassObject(c.AncestorIds[j]);
						if (a.GetMemberByNameIndex(m.NameIndex, upcase) != null)
						{
							bool ok = has_new || a.IsInterface || m.HasModifier(Modifier.Override) ||

								m.HasModifier(Modifier.Shadows); //VB.NET

							if (!ok)
							{
								// The keyword new is required on 'member1' because it hides inherited member 'member2'
								scripter.CreateWarningObjectEx(Errors.CS0108,
									m.FullName, a.FullName + "." + m.Name);
							}
						}
						else
						{
							if (has_new)
								// The member 'member' does not hide an inherited member. The new keyword is not required
								scripter.CreateWarningObjectEx(Errors.CS0109,
										m.FullName);
						}
					}
				}
				else if (op == OP_EVAL_TYPE)
				{
					ProcessEvalType(l);
					if (scripter.IsError())
						break;
				}
				else if (op == OP_ADD_UNDERLYING_TYPE)
				{
					int class_id = r.arg1;
					int underlying_type_id = r.arg2;
					ClassObject c = GetClassObject(class_id);
					ClassObject u = GetClassObject(underlying_type_id);

					c.UnderlyingType = u;
				}
				else if (op == OP_EVAL)
				{
					if ((this[n+1].op == OP_EVAL_TYPE) && (this[n+1].res == r.res))
					{
						r.op = OP_NOP;
						continue;
					}

					ProcessEvalOp(l);
					if (scripter.IsError())
						break;
				}
			}

			if (scripter.IsError())
				return;

			for (int i = init_n + 1; i <= Card; i++)
			{
				r = (ProgRec) prog[i];
				int op = r.op;
				n = i;
				if (op == OP_EVAL_BASE_TYPE)
				{
					ClassObject c = GetClassObject(r.arg1);
					ClassObject a = c.AncestorClass;
					if (a == null)
					{
						int ancestor_id = (int) StandardType.Object;
						a = GetClassObject(ancestor_id);
						c.AncestorIds.Add(ancestor_id);
					}

					ReplaceId(r.res, a.Id);

					if (r.arg2 != 0)
					{
						FunctionObject best;
						int default_constructor_id = a.FindConstructorId(null, null, out best);
						if (default_constructor_id != 0)
							ReplaceId(r.arg2, default_constructor_id);
					}

					r.op = OP_NOP;
				}
				else if (op == OP_CAST)
				{
					symbol_table[r.res].TypeId = r.arg1;
				}
				else if (op == OP_ASSIGN_NAME)
				{
					symbol_table[r.res].NameIndex = symbol_table[r.arg2].NameIndex;
				}
			}
		}

		void ProcessAddressOf()
		{
			int method_id = r.arg1;
			if (symbol_table[method_id].Kind == MemberKind.Ref)
			{
				int level = symbol_table[r.arg1].Level;
				int type_id = symbol_table[level].TypeId;

				ClassObject c = GetClassObjectEx(type_id);

				int idx = symbol_table[r.arg1].NameIndex;

				bool upcase = GetUpcase(n);

				MemberObject m = c.GetMemberByNameIndex(idx, upcase);
				if (m == null)
				{
					string member_name = symbol_table[r.res].Name;
					// The name 'name' does not exist in the class or namespace 'namespace'
					scripter.CreateErrorObjectEx(Errors.CS0103, member_name, c.Name);
					return;
				}
				method_id = m.Id;
			}

			if (symbol_table[method_id].Kind != MemberKind.Method)
			{
				//CS0118. '{0}' denotes a '{1}' where a '{2}' was expected.";
				scripter.CreateErrorObjectEx(Errors.CS0118,
					symbol_table[method_id].FullName,
					symbol_table[method_id].Kind.ToString(),
					"method");
				return;
			}
			FunctionObject method = GetFunctionObject(method_id);

			int delegate_type_id = 0;

			for (int j = symbol_table.Card; j >= 1; j--)
			{
				if (symbol_table[j].Kind == MemberKind.Type)
				{
					ClassObject c = GetClassObject(j);
					if (c.IsDelegate)
					{
						FunctionObject pattern_method = c.PatternMethod;
						if (pattern_method != null)
						{
							if (FunctionObject.CompareHeaders(method, pattern_method))
							{
								delegate_type_id = j;
								break;
							}
						}
					}
				}
			}

			if (delegate_type_id == 0)
			{
				bool ok = false;
				for (int j = n; j <= card; j++)
				{
					if (this[j].op == OP_PUSH && this[j].arg1 == r.res)
					{
						int sub_id = this[j].res;
						string method_name = symbol_table[sub_id].Name;
						if (method_name.Substring(0, 4) == "add_")
						{
							ok = true;
							FunctionObject f = GetFunctionObject(sub_id);
							int param_id = f.Param_Ids[0];
							delegate_type_id = symbol_table[param_id].TypeId;

							break;
						}
						else if (method_name.Substring(0, 7) == "remove_")
						{
							ok = true;
							FunctionObject f = GetFunctionObject(sub_id);
							int param_id = f.Param_Ids[0];
							delegate_type_id = symbol_table[param_id].TypeId;

							break;
						}
					}
				}

				if (!ok)
				{
					//"Delegate type not found";
					scripter.CreateErrorObject(Errors.VB00005);
					return;
				}
			}

			r.op = OP_CREATE_OBJECT;
			r.arg1 = delegate_type_id;
			r.arg2 = 0;

			int object_id = r.res;

			symbol_table[object_id].TypeId = delegate_type_id;

			n++;
			InsertOperators(n, 4);

			this[n].op = OP_PUSH;
            if (method.Static)
                this[n].arg1 = symbol_table[method_id].Level;
            else
            {
                this[n].arg1 = symbol_table.GetThisId(GetCurrMethodId());
            }
			this[n].arg2 = 0;
			this[n].res = delegate_type_id;

			n++;
			this[n].op = OP_PUSH;
			this[n].arg1 = method_id;
			this[n].arg2 = 0;
			this[n].res = delegate_type_id;

			n++;
			this[n].op = OP_PUSH;
			this[n].arg1 = object_id;
			this[n].arg2 = 0;
			this[n].res = 0;

			n++;
			this[n].op = OP_SETUP_DELEGATE;
			this[n].arg1 = 0;
			this[n].arg2 = 0;
			this[n].res = 0;

            /*
              359 Module:1 Line:79 		m.MH += new MenuHandler(t.Proc);
              360        CREATE OBJECT                      MenuHandler[  119]                        [    0]                    $$75[  273]
              361                 PUSH                                t[  267]                        [    0]             MenuHandler[  119]
              362                 PUSH                             Proc[  239]                        [    0]             MenuHandler[  119]
              363                 PUSH                             $$75[  273]                        [    0]                        [    0]
              364       SETUP DELEGATE                                 [    0]                        [    0]                        [    0]
              365                 PUSH                             $$75[  273]                        [    0]                  add_MH[  149]
              366                 PUSH                                m[  255]                        [    0]                        [    0]
              367                 CALL                           add_MH[  149]                    Void[    1]                                 init=86
            */

		}

		/// <summary>
		/// Assigns types p-code variables.
		/// </summary>
		public void SetTypes()
		{
			SetTypesEx(0);
		}

		/// <summary>
		/// Assigns types p-code variables.
		/// </summary>
		public void SetTypesEx(int init_n)
		{
			if (scripter.IsError()) return;

			FunctionObject f = null;

			for (int i = init_n + 1; i <= Card; i++)
			{
				r = (ProgRec) prog[i];
				int op = r.op;
				n = i;

				if (op == OP_ASSIGN_TYPE)
				{
					symbol_table[r.arg1].TypeId = r.arg2;
					ClassObject c = GetClassObject(r.arg2);

					if (c.Class_Kind == ClassKind.Namespace)
					{
						scripter.CreateErrorObjectEx(Errors.CS0118, c.Name, "namespace", "class");
						break;
					}

					MemberKind k = symbol_table[r.arg1].Kind;
					if (k == MemberKind.Method)
					{
						f = GetFunctionObject(r.arg1);
						if ((MemberObject.CompareAccessibility(c, f) < 0) && (c.Id != f.OwnerId))
						{
							if (f.Name == "op_Implicit")
								// return type is less accessible than operator
								scripter.CreateErrorObjectEx(Errors.CS0056, c.Name, "Implicit");
							else if (f.Name == "op_Explicit")
								// return type is less accessible than operator
								scripter.CreateErrorObjectEx(Errors.CS0056, c.Name, "Explicit");
							else if (PaxSystem.PosCh('$', f.Name) >= 0)
							{
								if (f.Owner.IsDelegate)
									scripter.CreateErrorObjectEx(Errors.CS0058, c.Name, f.Owner.Name);
							}
							else
							{
								// return type is less accessible than method
								scripter.CreateErrorObjectEx(Errors.CS0050, c.Name, f.Name);
							}
						}
					}
					else if (k == MemberKind.Field)
					{
						FieldObject fld = GetFieldObject(r.arg1);
						if ((MemberObject.CompareAccessibility(c, fld) < 0) && (c.Id != fld.OwnerId))
						// field type is less accessible than method
							scripter.CreateErrorObjectEx(Errors.CS0052, c.Name, fld.Name);
					}
					else if (k == MemberKind.Event)
					{
						EventObject e = GetEventObject(r.arg1);
						if ((MemberObject.CompareAccessibility(c, e) < 0) && (c.Id != e.OwnerId))
						{
								// property type is less accessible than method
								scripter.CreateErrorObjectEx(Errors.CS0053, c.Name, e.Name);
						}
						if (!c.IsDelegate)
								// property type is less accessible than method
								scripter.CreateErrorObjectEx(Errors.CS0066, e.Name);
					}
					else if (k == MemberKind.Property)
					{
						PropertyObject p = GetPropertyObject(r.arg1);
						if ((MemberObject.CompareAccessibility(c, p) < 0) && (c.Id != p.OwnerId))
						{
							if (p.ParamCount == 0)
						// property type is less accessible than method
								scripter.CreateErrorObjectEx(Errors.CS0053, c.Name, p.Name);
							else
						// indexer type is less accessible than method
								scripter.CreateErrorObjectEx(Errors.CS0054, c.Name, p.Name);
						}
					}
					else if (k == MemberKind.Var)
					{
						if ((f != null) && (f.HasParameter(r.arg1)))
						{
							if ((MemberObject.CompareAccessibility(c, f) < 0) && (c.Id != f.OwnerId))
							{
								if ((f.Name == "get_Item") || (f.Name == "set_Item"))
									scripter.CreateErrorObjectEx(Errors.CS0055, c.Name, f.Name);
								else if (f.Name == "op_Implicit")
									scripter.CreateErrorObjectEx(Errors.CS0057, c.Name, "Implicit");
								else if (f.Name == "op_Explicit")
									scripter.CreateErrorObjectEx(Errors.CS0057, c.Name, "Explicit");
								else if (PaxSystem.PosCh('$', f.Name) >= 0)
								{
									if (f.Owner.IsDelegate)
										scripter.CreateErrorObjectEx(Errors.CS0059, c.Name, f.Owner.Name);
								}
								else
								{
									// parameter type is less accessible than method
//									scripter.CreateErrorObjectEx(Errors.CS0051, c.Name, f.Name);
								}
							}
						}
					}
					r.op = OP_NOP;
				}
				else if (op == OP_TYPEOF)
				{
					symbol_table[r.res].TypeId = symbol_table.TYPE_CLASS_id;
				}
				else if (op == OP_CREATE_REF_TYPE)
				{
					int type_id = r.arg1;
					int level = symbol_table[type_id].Level;
					string ref_type_name = symbol_table[type_id].Name + "&";

					int ref_type_id = 0;
					for (int k=1; k <= symbol_table.Card; k++)
					{
						if ((symbol_table[k].Level == level) &&
							(symbol_table[k].Name == ref_type_name))
							{
								ref_type_id = k;
								break;
							}
					}

					if (ref_type_id > 0)
					{
						ReplaceId(r.res, ref_type_id);
					}
					else
					{
						PutVal(r.res,  GetVal(type_id));
						symbol_table[r.arg1].Kind = MemberKind.Type;
					}
				}
				else if (op == OP_SET_REF_TYPE)
				{
					int type_id = symbol_table[r.arg1].TypeId;
					int level = symbol_table[type_id].Level;
					string ref_type_name = symbol_table[type_id].Name + "&";

					int ref_type_id = 0;
					for (int k=1; k <= symbol_table.Card; k++)
					{
						if ((symbol_table[k].Level == level) &&
							(symbol_table[k].Name == ref_type_name))
							{
								ref_type_id = k;
								break;
							}
					}

					if (ref_type_id == 0)
					{
						ref_type_id = symbol_table.AppVar();
						PutVal(ref_type_id, GetVal(type_id));
						symbol_table[ref_type_id].Kind = MemberKind.Type;
						symbol_table[ref_type_id].Level = level;
						symbol_table[ref_type_id].Name = ref_type_name;
					}
					symbol_table[r.arg1].TypeId = ref_type_id;

					for (int k = n; k <= Card; k++)
					{
						if ((this[k].op == OP_SET_REF_TYPE) &&
							(this[k].arg1 == r.arg1))
								this[k].op = OP_NOP;
					}
				}
				else if (op == OP_CREATE_OBJECT)
				{
					symbol_table[r.res].TypeId = r.arg1;
				}
				else if (op == OP_AS)
				{
					symbol_table[r.res].TypeId = r.arg2;
				}
				else if (op == OP_ASSIGN && this[n - 1].op == OP_DECLARE_LOCAL_VARIABLE)
				{
					if (this[n - 1].arg1 == r.res && !GetExplicit(n))
						symbol_table[r.res].TypeId = symbol_table[r.arg2].TypeId;
				}
				else if (op == OP_ASSIGN && symbol_table[r.arg1].TypeId == (int) StandardType.Object)
				{
					if (!GetStrict(n))
						symbol_table[r.res].TypeId = symbol_table[r.arg2].TypeId;
				}
				else if (op == OP_ADD_IMPLEMENTS) // VB.NET
				{
					int member_id = r.arg1;
					int dest_id = r.res;

					MemberObject m = GetMemberObject(member_id);
					m.ImplementsId = dest_id;
				}
				else if (op == OP_BEGIN_CALL)
				{
					string s = symbol_table[r.arg1].Name;
					if (s.IndexOf('[') >= 0)
						r.op = OP_NOP;
				}
				else if (op == OP_ADD_ARRAY_RANGE)
				{
					ClassObject c = GetClassObject(r.arg1);
					c.RangeTypeId = r.arg2;
				}
				else if (op == OP_ADD_ARRAY_INDEX)
				{
					ClassObject c = GetClassObject(r.arg1);
					c.IndexTypeId = r.arg2;

					string s = symbol_table[r.arg2].Name;
					int type_id = symbol_table.LookupTypeByName(s, true);
					
					if (type_id == 0)
						type_id = symbol_table.OBJECT_CLASS_id;

					ClassObject c_index = GetClassObject(type_id);
					if (c_index.ImportedType == null)
						c.ImportedType = typeof(object);
					else
						c.ImportedType = c_index.ImportedType;
				}
				else if (op == OP_CALL)
				{
					string s = symbol_table[r.arg1].Name;
					if (s.IndexOf('[') >= 0)
					{
						r.op = OP_CREATE_ARRAY_INSTANCE;
						s = PaxSystem.GetElementTypeName(s);
						bool upcase = GetUpcase(n);
						int element_type_id = symbol_table.LookupTypeByName(s, upcase);
						if (element_type_id > 0)
							r.arg1 = element_type_id;
						else
						{
							scripter.CreateErrorObjectEx(Errors.UNDECLARED_IDENTIFIER, s);
							break;
						}
					}
				}
				else if (op == OP_CREATE_REFERENCE)
				{
					if (symbol_table[r.arg1].Kind == MemberKind.Type)
					{
						ClassObject c = GetClassObject(r.arg1);

						if (scripter.ForbiddenTypes.IndexOf(c.FullName) >= 0)
						{
							scripter.CreateErrorObjectEx(Errors.PAX0007, c.FullName);
						}

						int idx = symbol_table[r.res].NameIndex;
						string member_name = symbol_table[r.res].Name;

						bool upcase = GetUpcase(n);

						MemberObject m = c.GetMemberByNameIndex(idx, upcase);

						if (m == null)
						{
							if (c.IsNamespace && c.Imported)
								m = scripter.FindImportedNamespaceMember(c, member_name, upcase);
						}

						if (m == null)
						{
							if (GetLanguage(n) == PaxLanguage.Pascal && PaxSystem.StrEql(member_name, "Create"))
							{
								int type_id = this[n].arg1;
								int res_id = this[n].res;
								int object_id = AppVar(GetCurrMethodId(), type_id);

								if (this[n + 1].op == OP_BEGIN_CALL && this[n + 1].arg1 == this[n].res)
								{
									this[n].op = OP_CREATE_OBJECT;
									this[n].res = object_id;
									this[n + 1].arg1 = type_id; // begin call

									int j = n + 1;
									for (;;)
									{
										if (this[j].op == OP_PUSH && this[j].res == res_id)
										{
											this[j].res = type_id;
										}

										if (this[j].op == OP_CALL && this[j].arg1 == res_id)
										{
											this[j].arg1 = type_id;
											break;
										}
										j++;
									}

									this[j - 1].arg1 = object_id;
									ReplaceId(this[j].res, object_id);
									this[j].res = 0;

									continue;
								}
								else
								{
									this[n].op = OP_CREATE_OBJECT;
									this[n].res = object_id;
									n++;
									InsertOperators(n, 3);

									this[n].op = OP_BEGIN_CALL;
									this[n].arg1 = type_id;
									this[n].arg2 = 0;
									this[n].res = 0;

									n++;
									this[n].op = OP_PUSH;
									this[n].arg1 = object_id;
									this[n].arg2 = 0;
									this[n].res = 0;

									n++;
									this[n].op = OP_CALL;
									this[n].arg1 = type_id;
									this[n].arg2 = 0;
									this[n].res = 0;

									ReplaceId(res_id, object_id);
									n = n - 3;
									continue;
								}
							}

							// The name 'name' does not exist in the class or namespace 'namespace'
							scripter.CreateErrorObjectEx(Errors.CS0103, member_name, c.Name);
							break;
						}

						if (!m.Static)
						{
							if (this[n+1].op == OP_ADDRESS_OF) //VB.NET
							{
								//ok
							}
							else
							{
								m = c.GetStaticMemberByNameIndex(idx, upcase); // try to find static method
								if (m == null)
								{
									if (GetLanguage(n) == PaxLanguage.Pascal && PaxSystem.StrEql(member_name, "Create"))
									{
										int type_id = this[n].arg1;
										int res_id = this[n].res;
										int object_id = AppVar(GetCurrMethodId(), type_id);

										if (this[n + 1].op == OP_BEGIN_CALL && this[n + 1].arg1 == this[n].res)
										{
											this[n].op = OP_CREATE_OBJECT;
											this[n].res = object_id;
											this[n + 1].arg1 = type_id; // begin call

											int j = n + 1;
											for (;;)
											{
												if (this[j].op == OP_PUSH && this[j].res == res_id)
												{
													this[j].res = type_id;
												}

												if (this[j].op == OP_CALL && this[j].arg1 == res_id)
												{
													this[j].arg1 = type_id;
													break;
												}
												j++;
											}

											this[j - 1].arg1 = object_id;
											ReplaceId(this[j].res, object_id);
											this[j].res = 0;

											continue;
										}
										else
										{
											this[n].op = OP_CREATE_OBJECT;
											this[n].res = object_id;

											n++;
											InsertOperators(n, 3);

											this[n].op = OP_BEGIN_CALL;
											this[n].arg1 = type_id;
											this[n].arg2 = 0;
											this[n].res = 0;

											n++;
											this[n].op = OP_PUSH;
											this[n].arg1 = object_id;
											this[n].arg2 = 0;
											this[n].res = 0;

											n++;
											this[n].op = OP_CALL;
											this[n].arg1 = type_id;
											this[n].arg2 = 0;
											this[n].res = 0;

											ReplaceId(res_id, object_id);
											n = n - 3;
											continue;
										}
									}

									// An object reference is required for the nonstatic field, method, or property 'member'
									scripter.CreateErrorObjectEx(Errors.CS0120, member_name);
									break;
								}
							}
						}
						else // m is static
                        {
							int owner_id = m.OwnerId;
							string s = symbol_table[owner_id].FullName;
							if (scripter.CheckForbiddenNamespace(s))
								scripter.CreateErrorObjectEx(Errors.PAX0006, s);
						}

						r.op = OP_NOP;
						ReplaceIdEx(r.res, m.Id, n, true);
					}
				} // OP_CREATE_REFERENCE
			}
		}

		/// <summary>
		/// Returns 'true', if id represents an integral type.
		/// </summary>
		public static bool IsIntegralTypeId(int id)
		{
			return (id == (int) StandardType.Sbyte) ||
				   (id == (int) StandardType.Byte) ||
				   (id == (int) StandardType.Short) ||
				   (id == (int) StandardType.Ushort) ||
				   (id == (int) StandardType.Int) ||
				   (id == (int) StandardType.Uint) ||
				   (id == (int) StandardType.Long) ||
				   (id == (int) StandardType.Ulong) ||
				   (id == (int) StandardType.Char);
		}

		/// <summary>
		/// Returns 'true', if id represents a floating point type.
		/// </summary>
		public static bool IsFloatingPointTypeId(int id)
		{
			return (id == (int) StandardType.Float) ||
				   (id == (int) StandardType.Double);
		}

		/// <summary>
		/// Returns 'true', if id represents a numeric type.
		/// </summary>
		public static bool IsNumericTypeId(int id)
		{
			return IsIntegralTypeId(id) ||
				   IsFloatingPointTypeId(id) ||
				   (id == (int) StandardType.Decimal);
		}

		/// <summary>
		/// Returns 'true', if id represents a simple type.
		/// </summary>
		public static bool IsSimpleTypeId(int id)
		{
			return IsNumericTypeId(id) ||
				   (id == (int) StandardType.Bool);
		}

		/// <summary>
		/// Matches unary operator.
		/// </summary>
		bool TryUnaryOperator(int type_id, ClassObject c1)
		{
			ClassObject c = GetClassObject(type_id);

			if (scripter.MatchTypes(c1, c))
			{
				if (c1.Class_Kind == ClassKind.Enum)
					symbol_table[r.res].TypeId = c1.Id;
				else
					symbol_table[r.res].TypeId = c.Id;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Assigns result type of overloadable unary operator and inserts call
		/// of appropriate method which represents implementation of the given
		/// operator.
		/// </summary>
		bool TryOverloadableUnaryOperator(string operator_name,
										   ClassObject c1)
		{
			int operator_id = c1.FindOverloadableUnaryOperatorId(operator_name, r.arg1);

			if (operator_id != 0)
			{
				r.op = OP_NOP;
				int res = r.res;

				n++;
				InsertOperators(n, 3);

				this[n].op = OP_PUSH;
				this[n].arg1 = r.arg1;
				this[n].arg2 = 0;
				this[n].res = operator_id;

				n++;
				this[n].op = OP_PUSH;
				this[n].arg1 = c1.Id;
				this[n].arg2 = 0;
				this[n].res = 0;

				n++;
				this[n].op = OP_CALL_SIMPLE;
				this[n].arg1 = operator_id;
				this[n].arg2 = 1;
				this[n].res = res;
				symbol_table[this[n].res].TypeId = symbol_table[operator_id].TypeId;
			}
			return operator_id != 0;
		}

		/// <summary>
		/// Assigns result type of unary operator.
		/// </summary>
		bool TryDetailedUnaryOperator(AssocIntegers details, ClassObject c1)
		{
			for (int i = 0; i < details.Count; i++)
			{
				if (details.Items1[i] == c1.Id)
				{
					r.op = details.Items2[i];
					symbol_table[r.res].TypeId = c1.Id;
					return true;
				}
			}

			for (int i = 0; i < details.Count; i++)
			{
				if (TryUnaryOperator(details.Items1[i], c1))
				{
					r.op = details.Items2[i];
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Replaces "general" unary operator with appropriate "detailed"
		/// operator.
		/// </summary>
		bool SetupDetailedUnaryOperator(int op, string str_op, AssocIntegers details)
		{
			int t1 = symbol_table[r.arg1].TypeId;
			ClassObject c1 = GetClassObject(t1);

			object v = overloadable_unary_operators_str[op];
			if (v != null)
			{
				string operator_name = (string) v;
				if (TryOverloadableUnaryOperator(operator_name, c1))
					return true;
			}

			if (TryDetailedUnaryOperator(details, c1))
				return true;
			else
			{
				string name1 = symbol_table[t1].Name;
				// operator cannot be applied
				scripter.CreateErrorObjectEx(Errors.CS0023, str_op, name1);
				return false;
			}
		}

		/// <summary>
		/// Matches binary operator.
		/// </summary>
		bool TryBinaryOperator(int type_id, ClassObject c1, ClassObject c2)
		{
			ClassObject c = GetClassObject(type_id);

			if (scripter.MatchTypes(c1, c) &&
				scripter.MatchTypes(c2, c))
			{
				if (c1.Class_Kind == ClassKind.Enum)
					symbol_table[r.res].TypeId = c1.Id;
				else if (c2.Class_Kind == ClassKind.Enum)
					symbol_table[r.res].TypeId = c2.Id;
				else
					symbol_table[r.res].TypeId = c.Id;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Assigns result type of unary operator.
		/// </summary>
		bool TryDetailedBinaryOperator(AssocIntegers details, ClassObject c1, ClassObject c2)
		{
			for (int i = 0; i < details.Count; i++)
			{
				if (TryBinaryOperator(details.Items1[i], c1, c2))
				{
					r.op = details.Items2[i];
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Assigns result type of overloadable binary operator and inserts call
		/// of appropriate method which represents implementation of the given
		/// operator.
		/// </summary>
		bool TryOverloadableBinaryOperator(int op, string operator_name,
										   ClassObject c1, ClassObject c2)
		{
			int operator_id;

			foreach	(string key in scripter.OperatorHelpers.Keys)
			{
				ClassObject c = null;
				FunctionObject best = null;
				int res_id = 0;
				IntegerList applicable_list = new IntegerList(false);
				IntegerList a = new IntegerList(true);
				a.Add(r.arg1);
				a.Add(r.arg2);
				a.Add(symbol_table.FALSE_id);
				IntegerList param_mod = new IntegerList(true);
				param_mod.Add((int) ParamMod.None);
				param_mod.Add((int) ParamMod.None);
				param_mod.Add((int) ParamMod.None);

				if (operator_name == key)
				{
					MethodInfo info = (MethodInfo) scripter.OperatorHelpers[key];
					Type t = info.ReflectedType;
					int type_id = symbol_table.RegisterType(t, false);
					int sub_id = symbol_table.RegisterMethod(info, type_id);
					c = scripter.GetClassObject(type_id);
					FunctionObject f = scripter.GetFunctionObject(sub_id);
					c.AddApplicableMethod(f, a, param_mod, res_id, ref best, ref applicable_list);
				}

				operator_id = 0;
				if (c != null)
				{
					c.CompressApplicableMethodList(a, applicable_list);
					if (applicable_list.Count >= 1)
						operator_id = applicable_list[0];
				}

				if (operator_id != 0)
				{
					r.op = OP_NOP;
					int res = r.res;

					n++;
					InsertOperators(n, 5);

					this[n].op = OP_PUSH;
					this[n].arg1 = a[0];
					this[n].arg2 = 0;
					this[n].res = operator_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = a[1];
					this[n].arg2 = 0;
					this[n].res = operator_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = a[2];
					this[n].arg2 = 0;
					this[n].res = operator_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = c.Id;
					this[n].arg2 = 0;
					this[n].res = 0;

					n++;
					this[n].op = OP_CALL_SIMPLE;
					this[n].arg1 = operator_id;
					this[n].arg2 = 2;
					this[n].res = res;
					symbol_table[this[n].res].TypeId = symbol_table[operator_id].TypeId;

					return true;
				}
			}

			foreach	(string key in scripter.OperatorHelpers.Keys)
			{
				ClassObject c = null;
				FunctionObject best = null;
				int res_id = 0;
				IntegerList applicable_list = new IntegerList(false);
				IntegerList a = new IntegerList(true);
				a.Add(r.arg2);
				a.Add(r.arg1);
				a.Add(symbol_table.TRUE_id);
				IntegerList param_mod = new IntegerList(true);
				param_mod.Add((int) ParamMod.None);
				param_mod.Add((int) ParamMod.None);
				param_mod.Add((int) ParamMod.None);

				if (operator_name == key)
				{
					MethodInfo info = (MethodInfo) scripter.OperatorHelpers[key];
					Type t = info.ReflectedType;
					int type_id = symbol_table.RegisterType(t, false);
					int sub_id = symbol_table.RegisterMethod(info, type_id);
					c = scripter.GetClassObject(type_id);
					FunctionObject f = scripter.GetFunctionObject(sub_id);
					c.AddApplicableMethod(f, a, param_mod, res_id, ref best, ref applicable_list);
				}

				operator_id = 0;
				if (c != null)
				{
					c.CompressApplicableMethodList(a, applicable_list);
					if (applicable_list.Count >= 1)
						operator_id = applicable_list[0];
				}

				if (operator_id != 0)
				{
					r.op = OP_NOP;
					int res = r.res;

					n++;
					InsertOperators(n, 5);

					this[n].op = OP_PUSH;
					this[n].arg1 = a[0];
					this[n].arg2 = 0;
					this[n].res = operator_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = a[1];
					this[n].arg2 = 0;
					this[n].res = operator_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = a[2];
					this[n].arg2 = 0;
					this[n].res = operator_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = c.Id;
					this[n].arg2 = 0;
					this[n].res = 0;

					n++;
					this[n].op = OP_CALL_SIMPLE;
					this[n].arg1 = operator_id;
					this[n].arg2 = 2;
					this[n].res = res;
					symbol_table[this[n].res].TypeId = symbol_table[operator_id].TypeId;

					return true;
				}
			}

			operator_id = c1.FindOverloadableBinaryOperatorId(operator_name, r.arg1, r.arg2);
			if (operator_id != 0)
			{
				r.op = OP_NOP;
				int res = r.res;

				n++;
				InsertOperators(n, 4);

				this[n].op = OP_PUSH;
				this[n].arg1 = r.arg1;
				this[n].arg2 = 0;
				this[n].res = operator_id;

				n++;
				this[n].op = OP_PUSH;
				this[n].arg1 = r.arg2;
				this[n].arg2 = 0;
				this[n].res = operator_id;

				n++;
				this[n].op = OP_PUSH;
				this[n].arg1 = c1.Id;
				this[n].arg2 = 0;
				this[n].res = 0;

				n++;
				this[n].op = OP_CALL_SIMPLE;
				this[n].arg1 = operator_id;
				this[n].arg2 = 2;
				this[n].res = res;
				symbol_table[this[n].res].TypeId = symbol_table[operator_id].TypeId;

				return true;
			}

			operator_id = c2.FindOverloadableBinaryOperatorId(operator_name, r.arg2, r.arg1);
			if (operator_id != 0)
			{
				// swap arguments
				r.op = OP_SWAPPED_ARGUMENTS;
				int res = r.res;

				n++;
				InsertOperators(n, 5);

				this[n].op = OP_PUSH;
				this[n].arg1 = r.arg2;
				this[n].arg2 = 0;
				this[n].res = operator_id;

				n++;
				this[n].op = OP_PUSH;
				this[n].arg1 = r.arg1;
				this[n].arg2 = 0;
				this[n].res = operator_id;

				n++;
				this[n].op = OP_PUSH;
				this[n].arg1 = c1.Id;
				this[n].arg2 = 0;
				this[n].res = 0;

				n++;
				this[n].op = OP_CALL_SIMPLE;
				this[n].arg1 = operator_id;
				this[n].arg2 = 2;
				this[n].res = res;
				symbol_table[this[n].res].TypeId = symbol_table[operator_id].TypeId;

				r.arg1 = symbol_table.TRUE_id;

				n++;
				this[n].op = OP_SWAPPED_ARGUMENTS;
				this[n].arg1 = symbol_table.FALSE_id;;
				this[n].arg2 = 0;
				this[n].res = 0;

				return true;
			}


			return false;
		}

		/// <summary>
		/// Replaces "general" binary operator with appropriate "detailed"
		/// operator.
		/// </summary>
		bool SetupDetailedBinaryOperator(int op, string str_op, AssocIntegers details)
		{
			int t1 = symbol_table[r.arg1].TypeId;
			int t2 = symbol_table[r.arg2].TypeId;
			ClassObject c1 = GetClassObject(t1);

			c1 = GetClassObject(t1);
			if (c1.IsSubrange)
			{
				t1 = c1.AncestorIds[0];
				c1 = GetClassObject(t1);
			}

			ClassObject c2 = GetClassObject(t2);
			object v = overloadable_binary_operators_str[op];
			if (v != null)
			{
				string operator_name = (string) v;
				if (TryOverloadableBinaryOperator(op, operator_name, c1, c2))
					return true;
			}

			if (scripter.conversion.ExistsImplicitNumericConstConversion(scripter, r.arg1, r.arg2))
			{
				// r.arg1 is constant
				if (TryDetailedBinaryOperator(details, c2, c2))
					return true;
			}

			if (scripter.conversion.ExistsImplicitNumericConstConversion(scripter, r.arg2, r.arg1))
			{
				// r.arg2 is constant
				if (TryDetailedBinaryOperator(details, c1, c1))
					return true;
			}

			if (TryDetailedBinaryOperator(details, c1, c2))
				return true;
			else
			{
				if ((t1 == (int) StandardType.String) || (t2 == (int) StandardType.String))
				{
					if (op == OP_PLUS)
					{
						r.op = OP_ADDITION_STRING;
						symbol_table[r.res].TypeId = (int) StandardType.String;
						return true;
					}
				}

				if (op == OP_EQ || op == OP_NE)
				{
					if (t1 == t2)
						return true;
					if (t1 == (int) StandardType.Object)
						return true;
					if (t2 == (int) StandardType.Object)
						return true;
				}

				string name1 = symbol_table[t1].Name;
				string name2 = symbol_table[t2].Name;
				// operator cannot be applied
				scripter.CreateErrorObjectEx(Errors.CS0019, str_op, name1, name2);
				return false;
			}
		}

		/// <summary>
		/// Inserts operators which implement passing of 'params' parameter.
		/// </summary>
		void InsertActualArray(FunctionObject f, int curr_level, IntegerList pos, IntegerList a)
		{
			// this[n].op == OP_CALL
			this[n].arg2 = f.ParamCount;
			int k = a.Count - f.ParamCount;
			int array_type_id = symbol_table[f.ParamsId].TypeId;
			string element_type_name = PaxSystem.GetElementTypeName(symbol_table[array_type_id].Name);
			int element_type_id = scripter.GetTypeId(element_type_name);
			int element_id = 0;

			if (element_type_id == (int) StandardType.Char && f.ParamCount == 1)
			{
				element_id = AppVar(curr_level, element_type_id);
				n = pos[pos.Count-1];

				int value_id = this[n].arg1;

				InsertOperators(n, 1);

				this[n].op = OP_TO_CHAR_ARRAY;
				this[n].arg1 = element_id;
				this[n].arg2 = value_id;
				this[n].res = element_id;

				n++;
				this[n].arg1 = element_id;

				n++;
				n++;

				return;
			}

			int array_id = AppVar(curr_level, array_type_id);
			int array_length_id = symbol_table.AppIntegerConst(k + 1);

			ClassObject c = GetClassObject(array_type_id);
			IntegerList l = new IntegerList(false);
			l.Add(array_length_id);

			IntegerList mod = new IntegerList(false);
			mod.Add(0);
/*
			FunctionObject best;

			int constructor_id = c.FindConstructorId(l, mod, out best);

			if (constructor_id == 0)
			{
				scripter.CreateErrorObjectEx(Errors.CS0143, c.Name);
			}
*/
			n--;
			InsertOperators(n, 4 + 4 * (k + 1) + 1);

			this[n].op = OP_CREATE_OBJECT;
			this[n].arg1 = array_type_id;
			this[n].arg2 = 0;
			this[n].res = array_id;

			n++;
			this[n].op = OP_PUSH;
			this[n].arg1 = array_length_id;
			this[n].arg2 = 0;
			this[n].res = 0; // constructor_id;

			n++;
			this[n].op = OP_PUSH;
			this[n].arg1 = array_id;
			this[n].arg2 = 0;
			this[n].res = 0;

			n++;
//			this[n].op = OP_CALL;
			this[n].op = OP_CREATE_ARRAY_INSTANCE;
//			this[n].arg1 = constructor_id;
			this[n].arg1 = element_type_id;
			this[n].arg2 = 1;
			this[n].res = 0;

			element_id = 0;
			int count = 0;
			for (int i = 1; i <= a.Count; i++)
			{
				if (i >= f.ParamCount)
				{
					this[pos[i-1]].op = OP_NOP;

					element_id = AppVar(curr_level, element_type_id);
					symbol_table[element_id].Kind = MemberKind.Index;

					int index_val_id = symbol_table.AppIntegerConst(count++);

					n++;
					this[n].op = OP_CREATE_INDEX_OBJECT;
					this[n].arg1 = array_id;
					this[n].arg2 = 0;
					this[n].res = element_id;

					n++;
					this[n].op = OP_ADD_INDEX;
					this[n].arg1 = element_id;
					this[n].arg2 = index_val_id;
					this[n].res = array_id;

					n++;
					this[n].op = OP_SETUP_INDEX_OBJECT;
					this[n].arg1 = element_id;
					this[n].arg2 = 0;
					this[n].res = array_id;

					n++;
					this[n].op = OP_ASSIGN;
					this[n].arg1 = element_id;
					this[n].arg2 = a[i - 1];
					this[n].res = element_id;
				}
			}

			n++;
			this[n].op = OP_PUSH;
			this[n].arg1 = array_id;
			this[n].arg2 = 0;
			this[n].res = f.Id;

			n++;
			n++;
		}

		/// <summary>
		/// Inserts numeric conversion.
		/// </summary>
		void InsertNumericConversion(int dest_type_id, int arg_number)
		{
			if (this[n - 1].op == OP_NOP)
				n--;
			else
				InsertOperators(n, 1);

			int res = AppVar(symbol_table[r.res].Level, symbol_table[r.res].TypeId);
			this[n].arg1 = 0;
			this[n].res = res;

			if (arg_number == 1)
			{
				this[n].arg2 = r.arg1;
				r.arg1 = res;
			}
			else
			{
				this[n].arg2 = r.arg2;
				r.arg2 = res;
			}

			int t = dest_type_id;
			if (t == (int) StandardType.Sbyte)
				this[n].op = OP_TO_SBYTE;
			else if (t == (int) StandardType.Byte)
				this[n].op = OP_TO_BYTE;
			else if (t == (int) StandardType.Uint)
				this[n].op = OP_TO_UINT;
			else if (t == (int) StandardType.Int)
				this[n].op = OP_TO_INT;
			else if (t == (int) StandardType.Ushort)
				this[n].op = OP_TO_USHORT;
			else if (t == (int) StandardType.Short)
				this[n].op = OP_TO_SHORT;
			else if (t == (int) StandardType.Ulong)
				this[n].op = OP_TO_ULONG;
			else if (t == (int) StandardType.Long)
				this[n].op = OP_TO_LONG;
			else if (t == (int) StandardType.Char)
				this[n].op = OP_TO_CHAR;
			else if (t == (int) StandardType.Float)
				this[n].op = OP_TO_FLOAT;
			else if (t == (int) StandardType.Decimal)
				this[n].op = OP_TO_DECIMAL;
			else if (t == (int) StandardType.Double)
				this[n].op = OP_TO_DOUBLE;
			n++;
		}

		/// <summary>
		/// Creates "pattern" method for a delegate class.
		/// </summary>
		public void CreatePatternMethod(FunctionObject p, FunctionObject g)
		{
			symbol_table[p.Id].TypeId = symbol_table[g.Id].TypeId;
			symbol_table[p.ResultId].TypeId = symbol_table[g.Id].TypeId;

			for (int i = 0; i < g.ParamCount; i++)
			{
				int gparam_id = g.Param_Ids[i];
				int pparam_id = AppVar(p.Id);
				symbol_table[pparam_id].TypeId = symbol_table[gparam_id].TypeId;
				symbol_table[pparam_id].Name = symbol_table[gparam_id].Name;

				p.Param_Ids.Add(pparam_id);
				p.Param_Mod.Add((int) ParamMod.None);
			}

			int code_id = AppVar(p.Id);
			int data_id = AppVar(p.Id);
			int res_id = p.ResultId;
			int this_id = p.ThisId;

			int break_label = AppLabel();
			int continue_label = AppLabel();

			int init = AddInstruction(OP_FIND_FIRST_DELEGATE, this_id, code_id, data_id);
			AddInstruction(OP_GO_NULL, break_label, code_id, 0);
			for (int i = 0; i < p.ParamCount; i++)
				AddInstruction(OP_PUSH, p.Param_Ids[i], p.Param_Mod[i], code_id);
			AddInstruction(OP_PUSH, data_id, 0, 0);
			AddInstruction(OP_CALL_SIMPLE, code_id, p.ParamCount, res_id);

			SetLabelHere(continue_label);

			AddInstruction(OP_FIND_NEXT_DELEGATE, this_id, code_id, data_id);
			AddInstruction(OP_GO_NULL, break_label, code_id, 0);

			for (int i = 0; i < p.ParamCount; i++)
				AddInstruction(OP_PUSH, p.Param_Ids[i], p.Param_Mod[i], code_id);
			AddInstruction(OP_PUSH, data_id, 0, 0);
			AddInstruction(OP_CALL_SIMPLE, code_id, p.ParamCount, res_id);

			AddInstruction(OP_GO, continue_label, 0, 0);

			SetLabelHere(break_label);
			AddInstruction(OP_RET, p.Id, 0, 0);

			p.Init = this[init];

		}

		/// <summary>
		/// Creates "pattern" method for a delegate class.
		/// </summary>
		public void CreatePatternMethod(Delegate instance)
		{
			Type t = instance.GetType();
			int type_id = symbol_table.RegisterType(t, true);
			ClassObject c = GetClassObject(type_id);
			if (c.PatternMethod.Init == null)
			{
#if full
				FunctionObject p = c.PatternMethod;
				MethodInfo method_info = instance.Method;
				t = method_info.DeclaringType;
				type_id = symbol_table.RegisterType(t, false);
				int sub_id = symbol_table.RegisterMethod(method_info, type_id);
				FunctionObject g = GetFunctionObject(sub_id);
				CreatePatternMethod(p, g);
#endif
			}
		}

		/// <summary>
		/// Processes OP_CAST.
		/// </summary>
		void CheckOP_CAST()
		{
			bool ok = false;
			int t2 = symbol_table[r.arg2].TypeId;

			ClassObject dest_type = GetClassObject(r.arg1);
			ClassObject source_type = GetClassObject(t2);

			if (dest_type == source_type)
			{
				ok = true;
				if (dest_type.IsEnum)
				{
					r.op = OP_TO_ENUM;
				}
			}
			else if (IsNumericTypeId(source_type.Id) && IsNumericTypeId(dest_type.Id))
			{
				ok = true;
				int t = dest_type.Id;
				if (t == (int) StandardType.Sbyte)
					r.op = OP_TO_SBYTE;
				else if (t == (int) StandardType.Byte)
					r.op = OP_TO_BYTE;
				else if (t == (int) StandardType.Uint)
					r.op = OP_TO_UINT;
				else if (t == (int) StandardType.Int)
					r.op = OP_TO_INT;
				else if (t == (int) StandardType.Ushort)
					r.op = OP_TO_USHORT;
				else if (t == (int) StandardType.Short)
					r.op = OP_TO_SHORT;
				else if (t == (int) StandardType.Ulong)
					r.op = OP_TO_ULONG;
				else if (t == (int) StandardType.Long)
					r.op = OP_TO_LONG;
				else if (t == (int) StandardType.Char)
					r.op = OP_TO_CHAR;
				else if (t == (int) StandardType.Float)
					r.op = OP_TO_FLOAT;
				else if (t == (int) StandardType.Decimal)
					r.op = OP_TO_DECIMAL;
				else if (t == (int) StandardType.Double)
					r.op = OP_TO_DOUBLE;
			}
			else if (source_type.IsEnum && dest_type.IsEnum)
			{
				ok = true;
				r.op = OP_TO_ENUM;
			}
			else if (source_type.IsEnum && IsNumericTypeId(dest_type.Id))
			{
				ok = true;
				int t = dest_type.Id;
				if (t == (int) StandardType.Sbyte)
					r.op = OP_TO_SBYTE;
				else if (t == (int) StandardType.Byte)
					r.op = OP_TO_BYTE;
				else if (t == (int) StandardType.Uint)
					r.op = OP_TO_UINT;
				else if (t == (int) StandardType.Int)
					r.op = OP_TO_INT;
				else if (t == (int) StandardType.Ushort)
					r.op = OP_TO_USHORT;
				else if (t == (int) StandardType.Short)
					r.op = OP_TO_SHORT;
				else if (t == (int) StandardType.Ulong)
					r.op = OP_TO_ULONG;
				else if (t == (int) StandardType.Long)
					r.op = OP_TO_LONG;
				else if (t == (int) StandardType.Char)
					r.op = OP_TO_CHAR;
				else if (t == (int) StandardType.Float)
					r.op = OP_TO_FLOAT;
				else if (t == (int) StandardType.Decimal)
					r.op = OP_TO_DECIMAL;
				else if (t == (int) StandardType.Double)
					r.op = OP_TO_DOUBLE;
			}
			else if (IsNumericTypeId(source_type.Id) && dest_type.IsEnum)
			{
				ok = true;
				r.op = OP_TO_ENUM;
			}
			else if (source_type.Id == ObjectTypeId)
				ok = true;
			else if (scripter.conversion.ExistsImplicitReferenceConversion(source_type, dest_type))
				ok = true;
			else if (source_type.IsClass && dest_type.IsClass)
			{
				ok = dest_type.InheritsFrom(source_type);
				if (!ok)
					ok = source_type.InheritsFrom(dest_type);
			}
			else if (source_type.Id == symbol_table.DELEGATE_CLASS_id && dest_type.IsDelegate)
				ok = true;
			else if (source_type.IsClass && dest_type.IsInterface)
				ok = (!source_type.Sealed) && (!source_type.Implements(dest_type));
			else if (source_type.IsInterface && dest_type.IsClass)
				ok = (!dest_type.Sealed) || (dest_type.Implements(source_type));
			else if (source_type.IsInterface && dest_type.IsInterface)
				ok = (!dest_type.InheritsFrom(source_type));
			else if (dest_type.Id == (int) StandardType.Object)
				ok = true;

			if (!ok)
				ok = (dest_type.InheritsFrom(source_type));

			symbol_table[r.res].TypeId = dest_type.Id;

			if (!ok)
			{
				ClassObject c;
				int conversion_id = source_type.FindOverloadableExplicitOperatorId(r.res);

				if (conversion_id == 0)
				{
					conversion_id = dest_type.FindOverloadableExplicitOperatorId(r.res);
					c = dest_type;
				}
				else
					c = source_type;

				if (conversion_id == 0)
				{
					// Cannot convert type '{0}' to '{1}'
					scripter.CreateErrorObjectEx(Errors.CS0030, source_type.Name, dest_type.Name);
				}
				else
				{
					InsertOperators(n, 2);

					this[n].op = OP_PUSH;
					this[n].arg1 = r.arg2;
					this[n].arg2 = 0;
					this[n].res = conversion_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = c.Id;
					this[n].arg2 = 0;
					this[n].res = 0;

					n++;
					this[n].op = OP_CALL_SIMPLE;
					this[n].arg1 = conversion_id;
					this[n].arg2 = 1;
					this[n].res = r.res;
				}
			}
		}

		/// <summary>
		/// Processes OP_INC.
		/// </summary>
		void CheckOP_INC()
		{
			int op = r.op;
			int t1 = symbol_table[r.arg1].TypeId;

			ClassObject c1 = GetClassObject(t1);
			if (TryOverloadableUnaryOperator("op_Increment", c1))
				return;
			if (IsNumericTypeId(c1.Id))
			{
				if (SetupDetailedUnaryOperator(op, "++", detailed_inc_operators))
					symbol_table[r.res].TypeId = t1;
			}
			else if (c1.Class_Kind == ClassKind.Enum)
			{
				if (!TryDetailedUnaryOperator(detailed_inc_operators, c1.UnderlyingType))
				{
					string name1 = symbol_table[r.arg1].Name;
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0023, "++", name1);
				}
				else
					symbol_table[r.res].TypeId = t1;
			}
			else
			{
				string name1 = symbol_table[r.arg1].Name;
				scripter.CreateErrorObjectEx(Errors.CS0023, "++", name1);
			}
		}

		/// <summary>
		/// Processes OP_INC.
		/// </summary>
		void CheckOP_DEC()
		{
			int op = r.op;
			int t1 = symbol_table[r.arg1].TypeId;

			ClassObject c1 = GetClassObject(t1);
			if (TryOverloadableUnaryOperator("op_Decrement", c1))
				return;
			if (IsNumericTypeId(c1.Id))
			{
				if (SetupDetailedUnaryOperator(op, "--", detailed_dec_operators))
					symbol_table[r.res].TypeId = t1;
			}
			else if (c1.Class_Kind == ClassKind.Enum)
			{
				if (!TryDetailedUnaryOperator(detailed_dec_operators, c1.UnderlyingType))
				{
					string name1 = symbol_table[r.arg1].Name;
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0023, "--", name1);
				}
				else
					symbol_table[r.res].TypeId = t1;
			}
			else
			{
				string name1 = symbol_table[r.arg1].Name;
				// operator cannot be applied
				scripter.CreateErrorObjectEx(Errors.CS0023, "--", name1);
			}
		}


		/// <summary>
		/// Processes OP_ASSIGN_COND_TYPE.
		/// </summary>
		void CheckOP_ASSIGN_COND_TYPE()
		{
			int t1 = symbol_table[r.arg1].TypeId;
			int t2 = symbol_table[r.arg2].TypeId;
			int t = 0;
			if (t1 == t2)
				t = t1;
			else if (scripter.conversion.ExistsImplicitConversion(scripter, r.arg1, r.arg2) &&
					(!scripter.conversion.ExistsImplicitConversion(scripter, r.arg2, r.arg1)))
			{
				t = t2;
			}
			else if (scripter.conversion.ExistsImplicitConversion(scripter, r.arg2, r.arg1) &&
					(!scripter.conversion.ExistsImplicitConversion(scripter, r.arg1, r.arg2)))
			{
				t = t1;
			}

			if (t == 0)
			{
				string type_name1 = symbol_table[t1].Name;
				string type_name2 = symbol_table[t2].Name;
				// Type of conditional expression can't be determined because there is no implicit conversion between 'class1' and 'class2'
				scripter.CreateErrorObjectEx(Errors.CS0173, type_name1, type_name2);
			}
			symbol_table[r.res].TypeId = t;
		}

		/// <summary>
		/// Processes OP_ASSIGN.
		/// </summary>
		void CheckOP_ASSIGN(ClassObject enum_class)
		{
			if (symbol_table[r.arg1].Kind == MemberKind.Index)
			{
				int owner_id = symbol_table[r.arg1].Level;
				if (symbol_table[owner_id].TypeId == (int) StandardType.String)
				{
					// Property or indexer 'property' cannot be assigned to — it is read only
					scripter.CreateErrorObjectEx(Errors.CS0200, "this");
					return;
				}
			}

			int t1 = symbol_table[r.arg1].TypeId;
			int t2 = symbol_table[r.arg2].TypeId;


			ClassObject c1, c2;

			c1 = GetClassObject(t1);
			if (c1.IsSubrange)
				t1 = c1.AncestorIds[0];

			if (t1 == t2)
			{
				// ok
			}
			else if (t2 == 0)
			{
				t2 = 0;
			}
			else if (t1 == (int) StandardType.Object)
			{
				// ok
			}
			else if (r.arg2 == symbol_table.NULL_id)
			{
				c1 = GetClassObject(t1);
				if (c1.IsValueType)
				{
					// cannot assign null
					scripter.CreateErrorObjectEx(Errors.CS0037, c1.Name);
					return;
				}
			}
			else if (enum_class != null) // initialization of an enum field
			{
				int type_base_id = enum_class.UnderlyingType.Id;
				c1 = GetClassObject(type_base_id);
				c2 = GetClassObject(t2);

				if (!scripter.MatchTypes(c2, c1))
				{
					// Cannot impllicitly convert type
					scripter.CreateErrorObjectEx(Errors.CS0029, c2.Name, c1.Name);
					return;
				}
				else
					InsertNumericConversion(type_base_id, 2);
			}
			else if (t1 == (int) StandardType.None)
			{
				symbol_table[r.arg1].TypeId = t2;
				if (t2 == (int) StandardType.None)
				{
					// Internal compiler error
					scripter.CreateErrorObject(Errors.CS0001);
					return;
				}
			}
			else
			{
				c1 = GetClassObject(t1);
				c2 = GetClassObject(t2);

				bool ok = c2.InheritsFrom(c1);

				if (!ok)
					ok = scripter.conversion.ExistsImplicitNumericConstConversion(scripter, r.arg2, r.arg1);

				if (!ok)
					ok = scripter.MatchTypes(c2, c1);

				if (!ok)
				{
					ClassObject c;
					int conversion_id = c1.FindOverloadableImplicitOperatorId(r.arg2, r.res);

					if (conversion_id == 0)
					{
						conversion_id = c2.FindOverloadableImplicitOperatorId(r.arg2, r.res);
						c = c2;
					}
					else
						c = c1;

					ok = (conversion_id > 0);

					if (ok)
					{
						InsertOperators(n, 2);

						this[n].op = OP_PUSH;
						this[n].arg1 = r.arg2;
						this[n].arg2 = 0;
						this[n].res = conversion_id;

						n++;
						this[n].op = OP_PUSH;
						this[n].arg1 = c.Id;
						this[n].arg2 = 0;
						this[n].res = 0;

						n++;
						this[n].op = OP_CALL_SIMPLE;
						this[n].arg1 = conversion_id;
						this[n].arg2 = 1;
						this[n].res = r.res;

						return;
					}
				}

				if (!ok)
				{
					if (PascalOrBasic(n))
                    {
						if (t1 == (int) StandardType.String && t2 == (int) StandardType.Char)
						{
							return;
						}
					}

					// Cannot impllicitly convert type
					scripter.CreateErrorObjectEx(Errors.CS0029, c2.Name, c1.Name);
					return;
				}
			}

			c1 = GetClassObject(t1);
			c2 = GetClassObject(t2);
			if ((t1 > symbol_table.OBJECT_CLASS_id) && (c1.Class_Kind == ClassKind.Struct))
			{
				r.op = OP_ASSIGN_STRUCT;
			}
			else if (IsNumericTypeId(c1.Id) && IsNumericTypeId(c2.Id))
			{
				if (symbol_table[r.arg1].Kind != MemberKind.Var || c1 != c2)
					InsertNumericConversion(c1.Id, 2);
			}
		}

		/// <summary>
		/// Processes OP_PLUS.
		/// </summary>
		void CheckOP_PLUS()
		{
			int op = r.op;
			int t1 = symbol_table[r.arg1].TypeId;
			int t2 = symbol_table[r.arg2].TypeId;
			ClassObject c1 = GetClassObject(t1);
			ClassObject c2 = GetClassObject(t2);
            if (t1 == t2)
            {
                if (t1 == (int)StandardType.String)
                {
                    r.op = OP_ADDITION_STRING;
                    symbol_table[r.res].TypeId = t1;
                    return;
                }
                if (t1 == (int)StandardType.Int)
                {
                    r.op = OP_ADDITION_INT;
                    symbol_table[r.res].TypeId = t1;
                    return;
                }
                if (t1 == (int)StandardType.Float)
                {
                    r.op = OP_ADDITION_FLOAT;
                    symbol_table[r.res].TypeId = t1;
                    return;
                }
                if (t1 == (int)StandardType.Decimal)
                {
                    r.op = OP_ADDITION_DECIMAL;
                    symbol_table[r.res].TypeId = t1;
                    return;
                }
            }
			if (c1.IsDelegate)
			{
				if (t1 == t2)
				{
					r.op = OP_ADD_DELEGATES;
					symbol_table[r.res].TypeId = t1;
					return;
				}
				else
				{
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019, "+", c1.Name, c2.Name);
					return;
				}
			}

			if (!SetupDetailedBinaryOperator(op, "+", detailed_addition_operators))
				return;
			if (r.op == OP_ADDITION_STRING)
			{
				if (c1.Id != (int) StandardType.String)
				{
					int res = AppVar(symbol_table[r.arg1].Level, (int) StandardType.String);
					FunctionObject best;

					bool upcase = GetUpcase();

					int sub_id = c1.FindMethodId("ToString", null, null, 0, out best, upcase);

					if (sub_id == 0)
					{
						// 'type' does not contain a definition for 'function'
						scripter.CreateErrorObjectEx(Errors.CS0117, c1.Name, "ToString");
						return;
					}

					InsertOperators(n, 2);

					this[n].op = OP_PUSH;
					this[n].arg1 = r.arg1;
					this[n].arg2 = 0;
					this[n].arg2 = 0;

					n++;
					this[n].op = OP_CALL;
					this[n].arg1 = sub_id;
					this[n].arg2 = 0;
					this[n].res = res;

					r.arg1 = res;
				}
				if (c2.Id != (int) StandardType.String)
				{
					int res = AppVar(symbol_table[r.arg2].Level, (int) StandardType.String);
					FunctionObject best;

					bool upcase = GetUpcase();

					int sub_id = c2.FindMethodId("ToString", null, null, 0, out best, upcase);

					if (sub_id == 0)
					{
						if (c2.IsEnum)
							sub_id = c2.UnderlyingType.FindMethodId("ToString", null, null, 0, out best, upcase);

						if (sub_id == 0)
						{
							// 'type' does not contain a definition for 'function'
							scripter.CreateErrorObjectEx(Errors.CS0117, c2.Name, "ToString");
							return;
						}
					}

					InsertOperators(n, 2);

					this[n].op = OP_PUSH;
					this[n].arg1 = r.arg2;
					this[n].arg2 = 0;
					this[n].arg2 = 0;

					n++;
					this[n].op = OP_CALL;
					this[n].arg1 = sub_id;
					this[n].arg2 = 0;
					this[n].res = res;

					r.arg2 = res;
				}
			}
			else if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				t1 = GetTypeId(r.arg1);
				t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_MINUS.
		/// </summary>
		void CheckOP_MINUS()
		{
			int op = r.op;
			int t1 = symbol_table[r.arg1].TypeId;
			int t2 = symbol_table[r.arg2].TypeId;
			ClassObject c1 = GetClassObject(t1);
			ClassObject c2 = GetClassObject(t2);
            if (t1 == t2)
            {
                if (t1 == (int)StandardType.Int)
                {
                    r.op = OP_SUBTRACTION_INT;
                    symbol_table[r.res].TypeId = t1;
                    return;
                }
                if (t1 == (int)StandardType.Float)
                {
                    r.op = OP_SUBTRACTION_FLOAT;
                    symbol_table[r.res].TypeId = t1;
                    return;
                }
                if (t1 == (int)StandardType.Decimal)
                {
                    r.op = OP_SUBTRACTION_DECIMAL;
                    symbol_table[r.res].TypeId = t1;
                    return;
                }
            }
            if (c1.IsDelegate)
			{
				if (t1 == t2)
				{
					r.op = OP_SUB_DELEGATES;
					symbol_table[r.res].TypeId = t1;
					return;
				}
				else
				{
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019, "-", c1.Name, c2.Name);
					return;
				}
			}
			if (!SetupDetailedBinaryOperator(op, "-", detailed_subtraction_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				t1 = GetTypeId(r.arg1);
				t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_MULT.
		/// </summary>
		void CheckOP_MULT()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "*", detailed_multiplication_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_EXPONENT.
		/// </summary>
		void CheckOP_EXP()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "^", detailed_exponent_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != (int) StandardType.Int && IsNumericTypeId(tr))
					InsertNumericConversion((int) StandardType.Int, 2);
			}
		}

		/// <summary>
		/// Processes OP_DIV.
		/// </summary>
		void CheckOP_DIV()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "/", detailed_division_operators))
				return;

			if (symbol_table[r.res].TypeId == 0)
				symbol_table[r.res].TypeId = symbol_table[r.arg1].TypeId;

			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_MOD.
		/// </summary>
		void CheckOP_MOD()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "%", detailed_remainder_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_LEFT_SHIFT.
		/// </summary>
		void CheckOP_LEFT_SHIFT()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "<<", detailed_left_shift_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_RIGHT_SHIFT.
		/// </summary>
		void CheckOP_RIGHT_SHIFT()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, ">>", detailed_right_shift_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_BITWISE_AND.
		/// </summary>
		void CheckOP_BITWISE_AND()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "&", detailed_bitwise_and_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_BITWISE_OR.
		/// </summary>
		void CheckOP_BITWISE_OR()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "|", detailed_bitwise_or_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_BITWISE_XOR.
		/// </summary>
		void CheckOP_BITWISE_XOR()
		{
			int op = r.op;
			if (!SetupDetailedBinaryOperator(op, "^", detailed_bitwise_xor_operators))
				return;
			if (this[n].op != OP_CALL_SIMPLE)
			{
				r = this[n];
				int t1 = GetTypeId(r.arg1);
				int t2 = GetTypeId(r.arg2);
				int tr = GetTypeId(r.res);
				if (t1 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 1);
				if (t2 != tr && IsNumericTypeId(tr))
					InsertNumericConversion(tr, 2);
			}
		}

		/// <summary>
		/// Processes OP_LOGICAL_AND.
		/// </summary>
		void CheckOP_LOGICAL_AND()
		{
			int card1 = symbol_table.Card;
			if (!SetupDetailedBinaryOperator(OP_BITWISE_AND, "&&",
											 detailed_bitwise_and_operators))
				return;
			int card2 = symbol_table.Card;
			if (card1 == card2) // no overloaded operator
			{
				int t1 = symbol_table[r.arg1].TypeId;
				int t2 = symbol_table[r.arg2].TypeId;
				if ((t1 == (int) StandardType.Bool) && (t2 == (int) StandardType.Bool))
				{
					int arg1 = r.arg1;
					int arg2 = r.arg2;
					int res = r.res;
					r.op = OP_NOP;
					int lf = symbol_table.AppLabel();
					int lg = symbol_table.AppLabel();

					n++;
					InsertOperators(n, 4);

					this[n].op = OP_GO_FALSE;
					this[n].arg1 = lf;
					this[n].arg2 = arg1;

					n++;
					this[n].op = OP_ASSIGN;
					this[n].arg1 = res;
					this[n].arg2 = arg2;
					this[n].res = res;

					n++;
					this[n].op = OP_GO;
					this[n].arg1 = lg;

					n++;
					this[n].op = OP_ASSIGN;
					this[n].arg1 = res;
					this[n].arg2 = symbol_table.FALSE_id;
					this[n].res = res;

					symbol_table.SetLabel(lf, n);
					n++;
					symbol_table.SetLabel(lg, n);
				}
				else
				{
					string name1 = symbol_table[r.arg1].Name;
					string name2 = symbol_table[r.arg2].Name;
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019, "&&", name1, name2);
				}
			}
		}

		/// <summary>
		/// Processes OP_LOGICAL_OR.
		/// </summary>
		void CheckOP_LOGICAL_OR()
		{
			int card1 = symbol_table.Card;
			if (!SetupDetailedBinaryOperator(OP_BITWISE_OR, "||",
											 detailed_bitwise_or_operators))
				return;
			int card2 = symbol_table.Card;
			if (card1 == card2) // no overloaded operator
			{
				int t1 = symbol_table[r.arg1].TypeId;
				int t2 = symbol_table[r.arg2].TypeId;
				if ((t1 == (int) StandardType.Bool) && (t2 == (int) StandardType.Bool))
				{
					int arg1 = r.arg1;
					int arg2 = r.arg2;
					int res = r.res;
					r.op = OP_NOP;
					int lt = symbol_table.AppLabel();
					int lg = symbol_table.AppLabel();

					n++;
					InsertOperators(n, 4);

					this[n].op = OP_GO_TRUE;
					this[n].arg1 = lt;
					this[n].arg2 = arg1;

					n++;
					this[n].op = OP_ASSIGN;
					this[n].arg1 = res;
					this[n].arg2 = arg2;
					this[n].res = res;

					n++;
					this[n].op = OP_GO;
					this[n].arg1 = lg;

					n++;
					this[n].op = OP_ASSIGN;
					this[n].arg1 = res;
					this[n].arg2 = symbol_table.TRUE_id;
					this[n].res = res;

					symbol_table.SetLabel(lt, n);
					n++;
					symbol_table.SetLabel(lg, n);
				}
				else
				{
					string name1 = symbol_table[r.arg1].Name;
					string name2 = symbol_table[r.arg2].Name;
					scripter.CreateErrorObjectEx(Errors.CS0019, "||", name1, name2);
				}
			}
		}


		/// <summary>
		/// Processes OP_EQ.
		/// </summary>
		void CheckOP_EQ()
		{
			int op = r.op;
			int t1 = symbol_table[r.arg1].TypeId;
			int t2 = symbol_table[r.arg2].TypeId;
            if (t1 == t2)
            {
                if (t1 == (int)StandardType.String)
                {
                    r.op = OP_EQ_STRING;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
                if (t1 == (int)StandardType.Int)
                {
                    r.op = OP_EQ_INT;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
                if (t1 == (int)StandardType.Float)
                {
                    r.op = OP_EQ_FLOAT;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
                if (t1 == (int)StandardType.Decimal)
                {
                    r.op = OP_EQ_DECIMAL;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
            }

			ClassObject c1 = GetClassObject(t1);
			ClassObject c2 = GetClassObject(t2);
			if (c1.IsDelegate)
			{
				if ((t1 == t2) || (t2 == symbol_table.OBJECT_CLASS_id))
				{
					r.op = OP_EQ_DELEGATES;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else
				{
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019, "==", c1.Name, c2.Name);
				}
				return;
			}
			else if (c2.IsDelegate)
			{
				if ((t1 == t2) || (t1 == symbol_table.OBJECT_CLASS_id))
				{
					r.op = OP_EQ_DELEGATES;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else
				{
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019, "==", c1.Name, c2.Name);
				}
				return;
			}
			if ((t1 == (int) StandardType.String) &&
					(r.arg2 == symbol_table.NULL_id))
			{
				r.op = OP_EQ_STRING;
			}
			else if ((t2 == (int) StandardType.String) &&
					(r.arg1 == symbol_table.NULL_id))
			{
				r.op = OP_EQ_STRING;
			}
			else if (r.arg1 == symbol_table.NULL_id)
			{
				if (c2.IsValueType)
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019,
						"==", "<null>", c2.Name);
			}
			else if (r.arg2 == symbol_table.NULL_id)
			{
				if (c1.IsValueType)
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019,
						"==", c1.Name, "<null>");
			}
			else if (!SetupDetailedBinaryOperator(op, "==", detailed_eq_operators))
			{
			}
			symbol_table[r.res].TypeId = (int) StandardType.Bool;
		}



		/// <summary>
		/// Processes OP_NE.
		/// </summary>
		void CheckOP_NE()
		{
			int op = r.op;
			int t1 = symbol_table[r.arg1].TypeId;
			int t2 = symbol_table[r.arg2].TypeId;
			ClassObject c1 = GetClassObject(t1);
			ClassObject c2 = GetClassObject(t2);
            if (t1 == t2)
            {
                if (t1 == (int)StandardType.String)
                {
                    r.op = OP_NE_STRING;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
                if (t1 == (int)StandardType.Int)
                {
                    r.op = OP_NE_INT;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
                if (t1 == (int)StandardType.Float)
                {
                    r.op = OP_NE_FLOAT;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
                if (t1 == (int)StandardType.Decimal)
                {
                    r.op = OP_NE_DECIMAL;
                    symbol_table[r.res].TypeId = (int)StandardType.Bool;
                    return;
                }
            }

			if (c1.IsDelegate)
			{
				if ((t1 == t2) || (t2 == symbol_table.OBJECT_CLASS_id))
				{
					r.op = OP_NE_DELEGATES;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else
				{
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019, "!=", c1.Name, c2.Name);
				}
				return;
			}
			else if (c2.IsDelegate)
			{
				if ((t1 == t2) || (t1 == symbol_table.OBJECT_CLASS_id))
				{
					r.op = OP_NE_DELEGATES;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else
				{
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019, "!=", c1.Name, c2.Name);
				}
				return;
			}
			if ((t1 == (int) StandardType.String) &&
					(r.arg2 == symbol_table.NULL_id))
			{
				r.op = OP_NE_STRING;
			}
			else if ((t2 == (int) StandardType.String) &&
					(r.arg1 == symbol_table.NULL_id))
			{
				r.op = OP_NE_STRING;
			}
			else if (r.arg1 == symbol_table.NULL_id)
			{
				if (c2.IsValueType)
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019,
						"!=", "<null>", c2.Name);
			}
			else if (r.arg2 == symbol_table.NULL_id)
			{
				if (c1.IsValueType)
					// operator cannot be applied
					scripter.CreateErrorObjectEx(Errors.CS0019,
						"!=", c1.Name, "<null>");
			}
			else if (!SetupDetailedBinaryOperator(op, "!=", detailed_ne_operators))
			{
			}
			symbol_table[r.res].TypeId = (int) StandardType.Bool;
		}

		/// <summary>
		/// Processes OP_CREATE_REFERENCE.
		/// </summary>
		void CheckOP_CREATE_REFERENCE(ClassObject current_class)
		{
			if (symbol_table[r.arg1].Kind != MemberKind.Type)
			{
				int type_id = symbol_table[r.arg1].TypeId;
				ClassObject c = GetClassObjectEx(type_id);

				int idx = symbol_table[r.res].NameIndex;
				string member_name = symbol_table[r.res].Name;

				bool upcase = GetUpcase(n);

				MemberObject m = c.GetMemberByNameIndex(idx, upcase);

				if (m == null && upcase && member_name.ToUpper() == "NEW" && PascalOrBasic(n))
				{
					FunctionObject best;
					IntegerList a = new IntegerList(false);
					IntegerList param_mod = new IntegerList(false);
					c.FindConstructorId(a, param_mod, out best);
					if (best != null)
						m = best;
				}

				if (m == null)
				{
					// The name 'name' does not exist in the class or namespace 'namespace'
					scripter.CreateErrorObjectEx(Errors.CS0103, member_name, c.Name);
					return;
				}
				if (m.Static)
				{
					m = c.GetInstanceMemberByNameIndex(idx, upcase); // try to find instance method
					if (m == null)
					{
						// Static member 'member' cannot be accessed with an instance reference; qualify it with a type name instead
						scripter.CreateErrorObjectEx(Errors.CS0176, member_name);
						return;
					}
				}
				if (m.Private)
				{
					if (current_class == null)
					{
						scripter.CreateErrorObject(Errors.CS0001);
						// Internal compiler error
						return;
					}
					else if (c.Id != current_class.Id)
					{
						// 'member' is inaccessible due to its protection level
						scripter.CreateErrorObjectEx(Errors.CS0122, m.Name);
						return;
					}
				}
				if (m.Protected)
				{
					if (current_class == null)
					{
						// Internal compiler error
						scripter.CreateErrorObject(Errors.CS0001);
						return;
					}
					else if (!
							((c.Id == current_class.Id) ||
							(current_class.InheritsFrom(c)))
							)
					{
						// 'member' is inaccessible due to its protection level
						scripter.CreateErrorObjectEx(Errors.CS0122, m.Name);
						return;
					}
				}
				if (m.Kind == MemberKind.Constructor)
				{
					ReplaceId(r.res, m.Id);
					r.op = OP_NOP;
				}
				else if (m.Kind == MemberKind.Method)
				{
					// find out, if it is a delegate constructor parameter
					int k = n - 1;
					while ((this[k].op == OP_NOP) || (this[k].op == OP_SEPARATOR))
						k--;

					bool b = false;
					ClassObject delegate_class = null;
					if (this[k].op == OP_BEGIN_CALL)
					{
						int arg1 = this[k].arg1;
						if (symbol_table[arg1].Kind == MemberKind.Type)
						{
							delegate_class = GetClassObject(arg1);
							b = delegate_class.IsDelegate;
						}
					}

					if (b)
					{
						FunctionObject best;
						FunctionObject pattern = delegate_class.PatternMethod;

						if (pattern.Init == null)
						{
							FunctionObject g = GetFunctionObject(m.Id);
							CreatePatternMethod(pattern, g);
						}

						int sub_id = c.FindMethodId(idx, pattern.Param_Ids, pattern.Param_Mod,
														pattern.ResultId, out best, upcase);
						if (sub_id == 0)
						{
							if (c.GetMemberByNameIndex(idx, upcase) != null)
							{
								// Method name expected
								scripter.CreateErrorObject(Errors.CS0149);
							}
							else
							{
								string delegate_name = delegate_class.Name;
								// Method 'method' does not match delegate 'delegate'
								scripter.CreateErrorObjectEx(Errors.CS0123, member_name, delegate_name);
							}
							return;
						}

						ReplaceId(r.res, sub_id);
						r.res = delegate_class.Id;
						r.op = OP_PUSH;
					}
					else
					{
						if (PascalOrBasic(n))
						{
							int object_id = r.arg1;
							int sub_id = m.Id;

							if (this[n + 1].op == OP_ADDRESS_OF)
							{
								r.op = OP_NOP;
								this[n + 1].arg1 = sub_id;
							}
							else
							{

								bool there_is_call = false;
								for (int j = n; j < card; j++)
								{
									int op = this[j].op;
									if (this[j].arg1 == r.res)
									{
										if (op == OP_CALL || op == OP_CALL_BASE || op == OP_CALL_VIRT || op == OP_CALL_SIMPLE)
										{
											there_is_call = true;
											break;
										}
									}
								}

								if (there_is_call)
								{
									r.op = OP_NOP;
								}
								else
								{
									InsertOperators(n, 2);

									this[n].op = OP_BEGIN_CALL;
									this[n].arg1 = sub_id;
									this[n].arg2 = 0;
									this[n].res = 0;

									n++;

									this[n].op = OP_PUSH;
									this[n].arg1 = object_id;
									this[n].arg2 = 0;
									this[n].res = 0;

									r.op = OP_CALL;
									r.arg1 = sub_id;
									r.arg2 = 0;
									symbol_table[r.res].Kind = MemberKind.Var;
								}
							}
						}
						else
							r.op = OP_NOP;
					}
				}
				else if (m.Kind == MemberKind.Event)
				{
					EventObject e = (EventObject) m;

					int k = 0;
					for (int j = n; j <= Card; j++)
					{
						if ((this[j].arg1 == r.res) && (this[j].op == OP_ASSIGN))
						{
							k = j;
							break;
						}
					}

					if (k == 0)
					{
						if (e.EventFieldId == 0)
						{
							scripter.CreateErrorObjectEx(Errors.CS0154, e.Name);
							return;
						}
						string event_field_name = symbol_table[e.EventFieldId].Name;
						symbol_table[r.res].Name = event_field_name;
					}
					else
					{
						k --;
						if (this[k].op == OP_PLUS)
						{
							if (e.AddId == 0)
							{
								scripter.CreateErrorObjectEx(Errors.CS0154, e.Name);
								return;
							}

							int sub_id = e.AddId;
							int del_id = this[k].arg2;
							int object_id = r.arg1;

							r.op = OP_NOP;

							this[k].op = OP_PUSH;
							this[k].arg1 = del_id;
							this[k].arg2 = 0;
							this[k].res = sub_id;

							k++;
							this[k].op = OP_PUSH;
							this[k].arg1 = object_id;
							this[k].arg2 = 0;
							this[k].res = 0;

							k++;
							this[k].op = OP_CALL_SIMPLE;
							this[k].arg1 = sub_id;
							this[k].arg2 = 1;
							this[k].res = 0;
						}
						else if (this[k].op == OP_MINUS)
						{
							if (e.RemoveId == 0)
							{
								scripter.CreateErrorObjectEx(Errors.CS0154, e.Name);
								return;
							}

							int sub_id = e.RemoveId;
							int del_id = this[k].arg2;
							int object_id = r.arg1;

							r.op = OP_NOP;

							this[k].op = OP_PUSH;
							this[k].arg1 = del_id;
							this[k].arg2 = 0;
							this[k].res = sub_id;

							k++;
							this[k].op = OP_PUSH;
							this[k].arg1 = object_id;
							this[k].arg2 = 0;
							this[k].res = 0;

							k++;
							this[k].op = OP_CALL_SIMPLE;
							this[k].arg1 = sub_id;
							this[k].arg2 = 1;
							this[k].res = 0;
						}
						else
						{
							// The event can only appear on the left hand side of += or -=
							scripter.CreateErrorObjectEx(Errors.CS0079, e.Name);
							return;
						}
					}
				}
				else if (m.Kind == MemberKind.Property)
				{
					PropertyObject p = (PropertyObject) m;

					int write_n = 0;
					bool call_prop = false;
					bool need_read = false;

					// determine n_write

					for (int j = n; j <= Card; j++)
					{
						if (this[j].arg1 == r.res)
						{
							if (this[j].op == OP_ASSIGN)
							{
								write_n = j;
								break;
							}
							else if (this[j].op == OP_CALL && PascalOrBasic(n))
							{
								if (this[j+ 1].op == OP_ASSIGN && this[j + 1].arg1 == this[j].res)
								{
									write_n = j + 1;
									call_prop = true;
									break;
								}
							}
							else
								need_read = true;
						}
					}

					if (write_n > 0)
					{
						if (p.WriteId == 0)
						{
							// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
							scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
							return;
						}

						FunctionObject f = GetFunctionObject(p.WriteId);

						// convert assignment

						if (call_prop) // VB.NET only
						{
							int val_id = this[write_n].arg2;

							ReplaceId(r.res, p.WriteId);
							r.op = OP_NOP;

							this[write_n - 2].arg1 = r.arg1; // push object
							this[write_n].op = OP_NOP;

							this[write_n - 1].arg2 ++; // increase passed param number

							if (f.ParamCount != this[write_n - 1].arg2)
							{
								// No overload for method 'method' takes 'number' arguments
//								scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, this[write_n - 1].arg2);
							}

							// insert value
							InsertOperators(write_n - 2, 1);
							this[write_n - 2].op = OP_PUSH;
							this[write_n - 2].arg1 = val_id;
							this[write_n - 2].arg2 = 0;
							this[write_n - 2].res = p.WriteId;

							return;
						}
						else
						{
							this[write_n].op = OP_PUSH;
							this[write_n].arg1 = this[write_n].arg2;
							this[write_n].arg2 = 0;
							this[write_n].res = p.WriteId;

							this[write_n + 1].op = OP_PUSH;
							this[write_n + 1].arg1 = r.arg1;
							this[write_n + 1].arg2 = 0;
							this[write_n + 1].res = 0;

							this[write_n + 2].op = OP_CALL_SIMPLE;
							this[write_n + 2].arg1 = p.WriteId;
							this[write_n + 2].arg2 = 1;
							this[write_n + 2].res = 0;

							if (need_read)
							{
								if (p.ReadId == 0)
								{
									// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
									scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
									return;
								}

								this[n - 1].op = OP_PUSH;
								this[n - 1].arg1 = r.arg1;
								this[n - 1].arg2 = 0;
								this[n - 1].res = 0;

								this[n].op = OP_CALL_SIMPLE;
								this[n].arg1 = p.ReadId;
								this[n].arg2 = 0;
								symbol_table[this[n].res].Kind = MemberKind.Var;
								symbol_table[this[n].res].TypeId = symbol_table[p.ReadId].TypeId;
							}
							else
								r.op = OP_NOP;

							if (f.ParamCount != 1)
							{
								// No overload for method 'method' takes 'number' arguments
								scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, 1);
							}
						}
					}
					else
					{
						// write_n = 0 here

						if (p.ReadId == 0)
						{
							// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
							scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
							return;
						}

						FunctionObject f = GetFunctionObject(p.ReadId);

						int nn = n;
						bool bb = false;
						for (;;)
						{
							nn++;
							if (nn == card)
								break;
							if (this[nn].op == OP_BEGIN_CALL && this[nn].arg1 == r.res)
							{
								bb = true;
								break;
							}
						}

//						if (this[n + 1].op == OP_BEGIN_CALL && this[n + 1].arg1 == r.res && PascalOrBasic(n))
						if (bb && PascalOrBasic(n))
						{
							ReplaceId(r.res, p.ReadId);
							r.op = OP_NOP;

							IntegerList pos = new IntegerList(true);
							IntegerList a = new IntegerList(true);

							int last_j = 0;

//							int j = n + 1;
							int j = nn;
							for (;;)
							{
								j++;
								if (this[j].op == OP_PUSH && this[j].res == p.ReadId)
								{
									pos.Add(j);
									a.Add(this[j].arg1);
								}

								if (this[j].op == OP_CALL && this[j].arg1 == p.ReadId)
								{
									this[j].tag = j;
									get_item_list.Add(this[j]);
									last_j = j;
									break;
								}
							}
							this[j - 1].arg1 = r.arg1;

							if (f.ParamCount != this[j].arg2)
							{
								if ((f.Owner as ClassObject).HasMethod(f.NameIndex, this[j].arg2))
								{
									return;
								}

								if (f.ParamCount == 0)
								{
									this[j].arg2 = 0;
									for (int z = 0; z < pos.Count; z++)
										this[pos[z]].op = OP_NOP;

									int res_id = f.ResultId;
									int res_type_id = symbol_table[res_id].TypeId;
									ClassObject ct = GetClassObject(res_type_id);
									int index = scripter.names.Add("get_Item");
									MemberObject mm = ct.GetMemberByNameIndex(index, true);
									if (mm == null)
									{
										// No overload for method 'method' takes 'number' arguments
										scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, this[j].arg2);
										return;
									}

									int target_id = this[j].res;
									int temp_target_id = AppVar(symbol_table[target_id].Level);
									symbol_table[temp_target_id].TypeId = res_type_id;

									this[j].res = temp_target_id;

									int sub_id = mm.Id;

									InsertOperators(j + 1, a.Count + 3);

									j++;
									this[j].op = OP_BEGIN_CALL;
									this[j].arg1 = sub_id;
									this[j].arg2 = 0;
									this[j].res = 0;

									for (int z = 0; z < pos.Count; z++)
									{
										j++;
										this[j].op = OP_PUSH;
										this[j].arg1 = a[z];
										this[j].arg2 = 0;
										this[j].res = sub_id;
									}
									j++;
									this[j].op = OP_PUSH;
									this[j].arg1 = temp_target_id;
									this[j].arg2 = 0;
									this[j].res = 0;

									j++;
									this[j].op = OP_CALL;
									this[j].arg1 = sub_id;
									this[j].arg2 = a.Count;
									this[j].res = target_id;

									f = GetFunctionObject(sub_id);
									res_id = f.ResultId;
									res_type_id = symbol_table[res_id].TypeId;
									symbol_table[target_id].TypeId = res_type_id;

									n = j - 1;

									return;
								}
								else

								// No overload for method 'method' takes 'number' arguments
								scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, this[j].arg2);
							}

							if (get_item_list.Count >= 2)
							{
								ProgRec r1 = get_item_list[get_item_list.Count - 2] as ProgRec;
								ProgRec r2 = get_item_list[get_item_list.Count - 1] as ProgRec;

								if (r1.arg1 == r2.arg1)
								{
									for (;;)
									{
										if (this[last_j + 1].op == OP_ASSIGN &&
											this[last_j + 1].arg1 == r1.res)
												break;
										last_j ++;
										if (last_j == card)
											return;
									}

									if (this[last_j + 1].op == OP_ASSIGN &&
										this[last_j + 1].arg1 == r1.res)
									{
										if (p.WriteId == 0)
										{
											// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
											scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
											return;
										}

										int rvalue_id = this[last_j + 1].arg2;

										this[last_j + 1].op = OP_NOP;
										int prev_j = r1.tag;

										PaxArrayList copy_list = new PaxArrayList();
										copy_list.Insert(0, this[prev_j].Clone()); // OP_CALL
										this[prev_j].op = OP_NOP;
										prev_j --;
										copy_list.Insert(0, this[prev_j].Clone()); // PUSH instance
										this[prev_j].op = OP_NOP;
										int kk = 0;
										for (;;)
										{
											prev_j--;
											if (this[prev_j].op == OP_PUSH && this[prev_j].res == r1.arg1)
											{
												copy_list.Insert(0, this[prev_j].Clone()); // PUSH instance
												this[prev_j].op = OP_NOP;
												kk++;
												if (kk == r1.arg2)
													break;
											}
										}

										last_j++;
										InsertOperators(last_j, copy_list.Count);

										this[last_j].op = OP_BEGIN_CALL;
										this[last_j].arg1 = p.WriteId;
										this[last_j].arg2 = 0;
										this[last_j].res = 0;

										// copy indexes
										for (kk = 0; kk <= copy_list.Count - 3; kk++)
										{
											last_j++;
											this[last_j].op = OP_PUSH;
											this[last_j].arg1 = (copy_list[kk] as ProgRec).arg1;
											this[last_j].arg2 = (copy_list[kk] as ProgRec).arg2;
											this[last_j].res = p.WriteId;
										}

										last_j++;
										this[last_j].op = OP_PUSH;
										this[last_j].arg1 = rvalue_id;
										this[last_j].arg2 = 0;
										this[last_j].res = p.WriteId;

										last_j++;
										this[last_j].op = OP_PUSH;
										this[last_j].arg1 = (copy_list[copy_list.Count - 2] as ProgRec).arg1;
										this[last_j].arg2 = 0;
										this[last_j].res = 0;

										last_j++;
										this[last_j].op = OP_CALL;
										this[last_j].arg1 = p.WriteId;
										this[last_j].arg2 = r1.arg2 + 1;
										this[last_j].res = 0;
									}
								}
							}

							return;
						}

						this[n - 1].op = OP_PUSH;
						this[n - 1].arg1 = r.arg1;

						r.op = OP_CALL_SIMPLE;
						r.arg1 = p.ReadId;
						r.arg2 = 0;
						symbol_table[r.res].Kind = MemberKind.Var;
						symbol_table[r.res].TypeId = symbol_table[p.ReadId].TypeId;

						if (f.ParamCount != 0)
						{
							// No overload for method 'method' takes 'number' arguments
							scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, 0);
						}
					}
				}
				int member_type_id = symbol_table[m.Id].TypeId;
				symbol_table[r.res].TypeId = member_type_id;
			}
		} // OP_CREATE_REFERENCE


		/// <summary>
		/// Processes OP_CALL.
		/// </summary>
		void CheckOP_CALL(ClassObject current_class,
						  int curr_level,	
						  IntegerList a,
						  IntegerList param_mod,
						  IntegerList pos)
		{
			int sub_id = r.arg1;
			int n_call = n;

			// find actual parameter list
			a.Clear();
			param_mod.Clear();
			pos.Clear();

			int retValCount = 0;

			int j = n;
			for (;;)
			{
				if (j <= 1)
				{
					//internal compiler error
					//scripter.CreateErrorObject(Errors.CS0001);
					//return;
                    j = 1;
                    break;
				}

				if ((this[j].op == OP_BEGIN_CALL) && (this[j].arg1 == sub_id))
					break;
				j--;
			}

			this[j].op = OP_NOP;
			
			for (;;)
			{
				if (this[j].op == OP_PUSH && this[j].res == sub_id && this[j].labeled == false)
				{
                    this[j].labeled = true;

					pos.Add(j);
					a.Add(this[j].arg1);
					param_mod.Add(this[j].arg2);

					if (this[j].arg2 > 0)
						retValCount ++;
				}
				if (j == n)
					break;
				j++;
			}

			MemberKind k = symbol_table[sub_id].Kind;
			if (k == MemberKind.Type)
			{
				ClassObject c = (ClassObject) GetVal(sub_id);
				if (c.IsDelegate)
				{
					if (a.Count == 1)
					{
						FunctionObject curr_method = GetFunctionObject(curr_level);
						InsertOperators(pos[0], 1);
						n++;

						this[pos[0]].op = OP_PUSH;
						this[pos[0]].arg1 = curr_method.ThisId;
						this[pos[0]].arg2 = 0;
						this[pos[0]].res = c.Id;

						a.Insert(0, curr_method.ThisId);
					}

					if (c.PatternMethod.Init == null)
					{
						FunctionObject p = c.PatternMethod;
						int g_id = a.Last;
						if (symbol_table[g_id].Kind == MemberKind.Ref)
						{
							int g_level = symbol_table[g_id].Level;
							int type_id = symbol_table[g_level].TypeId;
							ClassObject cls = GetClassObjectEx(type_id);
							int g_idx = symbol_table[g_id].NameIndex;

							bool upcase = GetUpcase(n);

							MemberObject m = cls.GetMemberByNameIndex(g_idx, upcase);
							ReplaceId(a.Last, m.Id);
							a.Last = m.Id;
						}

						FunctionObject g = GetFunctionObject(a.Last);
						CreatePatternMethod(p, g);
					}

					this[n].op = OP_SETUP_DELEGATE;
					this[n].arg1 = 0;
					this[n].arg2 = 0;
					this[n].res = 0;
					return;
				}
				else
				{
					if (r.res > 0)
					{
						if (a.Count == 1 && GetLanguage(n) == PaxLanguage.Pascal)
						{
							r.op = OP_CAST;
							r.arg2 = a[0];
							this[n-1].op = OP_NOP;
							this[pos[0]].op = OP_NOP;
							n--;
							return;
						}

						string s = symbol_table[r.arg1].Name;
						// 'construct1_name' denotes a 'construct1' which is not valid in the given context
						scripter.CreateErrorObjectEx(Errors.CS0119, s, "type");
						return;
					}

					FunctionObject best;
					sub_id = c.FindConstructorId(a, param_mod, out best);
					if (sub_id == 0)
					{
						string method_name = c.Name;
						if (best != null)
						{
							if (best.ParamCount == a.Count)
							{
								if ((best.ParamsId == 0) || (a.Count == 0))
								{
									// The best overloaded method match for '{0}' has some invalid arguments
									scripter.CreateErrorObjectEx(Errors.CS1502, method_name);
									return;
								}
								int actual_id = a[a.Count - 1];
								int actual_type_id = symbol_table[actual_id].TypeId;
								string actual_type_name = symbol_table[actual_type_id].Name;
								if (PaxSystem.GetRank(actual_type_name) != 0)
								{
									scripter.CreateErrorObjectEx(Errors.CS1502, method_name);
									return;
								}
								InsertActualArray(best, curr_level, pos, a);
								sub_id = best.Id;
								n_call = n;
							}
							else
							{
								if (a.Count < best.ParamCount)
								{
									if (best.ParamsId == 0)
									{
										// No overload for method 'method' takes 'number' arguments
										scripter.CreateErrorObjectEx(Errors.CS1501, method_name, a.Count);
										return;
									}
									else if (a.Count + 1 == best.ParamCount)
									{
										InsertActualArray(best, curr_level, pos, a);
										sub_id = best.Id;
										n_call = n;
									}
									else
									{
										// No overload for method 'method' takes 'number' arguments
										scripter.CreateErrorObjectEx(Errors.CS1501, method_name, a.Count);
										return;
									}
								}
								else
								{
									// No overload for method 'method' takes 'number' arguments
									scripter.CreateErrorObjectEx(Errors.CS1501, method_name, a.Count);
									return;

/*									InsertActualArray(best, curr_level, pos, a);
									sub_id = best.Id;
									n_call = n; */
								}
							}
						}
						else
						{
							// The type 'class' has no constructors defined
							scripter.CreateErrorObjectEx(Errors.CS0143, c.Name);
							return;
						}
					}
				}
			}
			else if (k == MemberKind.Method)
			{
				int name_index = symbol_table[sub_id].NameIndex;
				string method_name = symbol_table[sub_id].Name;

				int class_id = symbol_table[sub_id].Level;
				ClassObject c = GetClassObject(class_id);
				FunctionObject best;
				bool upcase = GetUpcase(n);

	again:

				sub_id = c.FindMethodId(name_index, a, param_mod, 0, out best, upcase);
				if (sub_id == 0)
				{
					if (best != null)
					{
						if (best.ParamCount == a.Count)
						{
							bool success = true;
							for (int z = 0; z < a.Count; z++)
							{
								int actual_param_id = a[z];
								int formal_param_id = best.GetParamId(z);
								bool ok = scripter.MatchAssignment(formal_param_id, actual_param_id);
								if (!ok)
								{
									int a_type_id = symbol_table[actual_param_id].TypeId;
									ClassObject actual_class = GetClassObject(a_type_id);
									int conversion_id = actual_class.FindOverloadableImplicitOperatorId(actual_param_id, formal_param_id);
									if (conversion_id == 0)
									{
										success = false;
										break;
									}

									int save_n = n;
									n = pos[z];

									int curr_sub_id = GetCurrMethodId();
									int res_id = AppVar(curr_sub_id, symbol_table[formal_param_id].TypeId);

									int dest_sub_id = this[n].res;

									this[n].arg1 = res_id;

									InsertOperators(n, 3);

									this[n].op = OP_PUSH;
									this[n].arg1 = actual_param_id;
									this[n].arg2 = 0;
									this[n].res = conversion_id;

									n++;
									this[n].op = OP_PUSH;
									this[n].arg1 = actual_class.Id;
									this[n].arg2 = 0;
									this[n].res = 0;

									n++;
									this[n].op = OP_CALL_SIMPLE;
									this[n].arg1 = conversion_id;
									this[n].arg2 = 1;
									this[n].res = res_id;

									n = save_n + 3;

									for (int z1 = z + 1; z1 < a.Count; z1++)
									{
										pos[z1] = pos[z1] + 3;
									}
								}
							}

							if (success)
							{
								sub_id = best.Id;
								n_call = n;
								goto process;
//								return;
							}

							if ((best.ParamsId == 0) || (a.Count == 0))
							{
								// The best overloaded method match for '{0}' has some invalid arguments
								scripter.CreateErrorObjectEx(Errors.CS1502, method_name);
								return;
							}
							int actual_id = a[a.Count - 1];
							int actual_type_id = symbol_table[actual_id].TypeId;
							string actual_type_name = symbol_table[actual_type_id].Name;
							if (PaxSystem.GetRank(actual_type_name) != 0)
							{
								scripter.CreateErrorObjectEx(Errors.CS1502, method_name);
								return;
							}
							InsertActualArray(best, curr_level, pos, a);
							sub_id = best.Id;
							n_call = n;
						}
						else
						{
							if (a.Count < best.ParamCount)
							{
								int def_param_count = best.DefaultParamCount;
								if (a.Count + def_param_count >= best.ParamCount)
								{
									int needed_param_count = best.ParamCount -  a.Count;
									int p;

									if (pos.Count > 0)
										p = pos[a.Count - 1] + 1;
									else
										p = n - 2;

									this[n].arg2 += needed_param_count;

									InsertOperators(p, needed_param_count);

									int i1 = best.ParamCount - needed_param_count;

									for (int k1 = 0; k1 < needed_param_count; k1++)
									{
										int default_value_id = best.Default_Ids[i1];
										a.Add(default_value_id);
										pos.Add(p);
										param_mod.Add(best.Param_Mod[i1]);

										this[p].op = OP_PUSH;
										this[p].arg1 = default_value_id;
										this[p].arg2 = 0;
										this[p].res = best.Id;

										p++;
										n++;
										i1++;
									}

									r = this[n];
									n_call = n;

									goto again;
								}

								if (best.ParamsId == 0)
								{
									if (a.Count + 1 == best.ParamCount &&
										GetTypeId(best.Param_Ids[best.ParamCount-1]) == (int) StandardType.Object)
									{
										int p;

										if (pos.Count > 0)
											p = pos[a.Count - 1] + 1;
										else
											p = n - 2;

										InsertOperators(p, 1);

										this[p].op = OP_PUSH;
										this[p].arg1 = symbol_table.NULL_id;
										this[p].arg2 = 0;
										this[p].res = 0;

										n++;
										sub_id = best.Id;
									}
									else
									{
										// No overload for method 'method' takes 'number' arguments
										scripter.CreateErrorObjectEx(Errors.CS1501, method_name, a.Count);
										return;
									}
								}
								else if (a.Count + 1 == best.ParamCount)
								{
									InsertActualArray(best, curr_level, pos, a);
									sub_id = best.Id;
									n_call = n;
								}
								else
								{
									// No overload for method 'method' takes 'number' arguments
									scripter.CreateErrorObjectEx(Errors.CS1501, method_name, a.Count);
									return;
								}
							}
							else
							{
								InsertActualArray(best, curr_level, pos, a);
								sub_id = best.Id;
								n_call = n;
							}
						}
					}
					else
						// 'type' does not contain a definition for 'function'
						scripter.CreateErrorObjectEx(Errors.CS0117, c.Name, method_name);

					if (sub_id == 0)
						return;
				}
			}
			else if (k == MemberKind.Constructor)
			{
				int class_id = symbol_table[sub_id].Level;
				ClassObject c = GetClassObject(class_id);

				FunctionObject best;
				sub_id = c.FindConstructorId(a, param_mod, out best);
				r.res = 0;
				if (sub_id == 0)
				{
					// The type 'class' has no constructors defined
					scripter.CreateErrorObjectEx(Errors.CS0143, c.Name);
				}
			}
			else if (k == MemberKind.Destructor)
			{
				int class_id = symbol_table[sub_id].Level;
				ClassObject c = GetClassObject(class_id);
				sub_id = c.FindDestructorId(a);
				r.res = 0;
				if (sub_id == 0)
				{
					// 'type' does not contain a definition for 'function'
					scripter.CreateErrorObjectEx(Errors.CS0117, c.Name, "~" + c.Name);
					return;
				}
			}
			else if (k == MemberKind.Ref)
			{
				// push object
				int object_id = symbol_table[sub_id].Level;
				int prev_arg1 = this[n-1].arg1;
				this[n-1].arg1 = object_id;

				// find method
				int name_index = symbol_table[sub_id].NameIndex;
				string method_name = symbol_table[sub_id].Name;

				int class_id = symbol_table[object_id].TypeId;
				if (symbol_table[class_id].Kind != MemberKind.Type)
				{
					string type_name = symbol_table[class_id].Name;
					// The type or namespace name 'type/namespace' could not be found (are you missing a using directive or an assembly reference?)
					scripter.CreateErrorObjectEx(Errors.CS0246, type_name);
					return;
				}

				ClassObject c = GetClassObjectEx(class_id);

				FunctionObject best;
				bool upcase = GetUpcase(n);

				sub_id = c.FindMethodId(name_index, a, param_mod, 0, out best, upcase);
				if (sub_id == 0)
				{
					if (best != null)
					{
						if (best.ParamCount == a.Count)
						{
							bool success = true;
							for (int z = 0; z < a.Count; z++)
							{
								int actual_param_id = a[z];
								int formal_param_id = best.GetParamId(z);
								bool ok = scripter.MatchAssignment(formal_param_id, actual_param_id);
								if (!ok)
								{
									int a_type_id = symbol_table[actual_param_id].TypeId;
									ClassObject actual_class = GetClassObject(a_type_id);
									int conversion_id = actual_class.FindOverloadableImplicitOperatorId(actual_param_id, formal_param_id);
									if (conversion_id == 0)
									{
										success = false;
										break;
									}

									int save_n = n;
									n = pos[z];

									int curr_sub_id = GetCurrMethodId();
									int res_id = AppVar(curr_sub_id, symbol_table[formal_param_id].TypeId);

									int dest_sub_id = this[n].res;

									this[n].arg1 = res_id;

									InsertOperators(n, 3);

									this[n].op = OP_PUSH;
									this[n].arg1 = actual_param_id;
									this[n].arg2 = 0;
									this[n].res = conversion_id;

									n++;
									this[n].op = OP_PUSH;
									this[n].arg1 = actual_class.Id;
									this[n].arg2 = 0;
									this[n].res = 0;

									n++;
									this[n].op = OP_CALL_SIMPLE;
									this[n].arg1 = conversion_id;
									this[n].arg2 = 1;
									this[n].res = res_id;

									n = save_n + 3;

									for (int z1 = z + 1; z1 < a.Count; z1++)
									{
										pos[z1] = pos[z1] + 3;
									}
								}
							}

							if (success)
							{
								goto lab;
							}

							if ((best.ParamsId == 0) || (a.Count == 0))
							{
								scripter.CreateErrorObjectEx(Errors.CS1502, method_name);
								return;
							}
							int actual_id = a[a.Count - 1];
							int actual_type_id = symbol_table[actual_id].TypeId;
							string actual_type_name = symbol_table[actual_type_id].Name;
							if (PaxSystem.GetRank(actual_type_name) != 0)
							{
								scripter.CreateErrorObjectEx(Errors.CS1502, method_name);
								return;
							}
							InsertActualArray(best, curr_level, pos, a);

lab:

							sub_id = best.Id;
							n_call = n;
						}
						else
						{
							if ((a.Count < best.ParamCount) || (best.ParamsId == 0))
							{
								// patch for protected Component.Dispose call
								if (best.Name == "Dispose")
								{
									for (int z = 0; z < pos.Count; z++)
									{
										this[pos[z]].op = OP_NOP;
									}
									this[n-1].op = OP_NOP; // 'this'
									this[n].op = OP_NOP; // op_call
									return;
								}

								// No overload for method 'method' takes 'number' arguments
								scripter.CreateErrorObjectEx(Errors.CS1501, method_name, a.Count);
								return;
							}
							InsertActualArray(best, curr_level, pos, a);
							sub_id = best.Id;
							n_call = n;
						}
					}
					else
					{
						// is it a delegate call?

						int type_id = symbol_table[r.arg1].TypeId;
						if (symbol_table[type_id].Kind == MemberKind.Type)
						{
							ClassObject d = GetClassObject(type_id);

							if ((d.IsArray || d.IsPascalArray) && PascalOrBasic(n))
							{
								ConvertCallToIndexAccess(a, pos);
								return;
							}

							if (d.DefaultProperty != null && PascalOrBasic(n))
							{
								ConvertToDefaultPropertyCall(d.DefaultProperty, a, pos);
								return;
							}

							if (!d.IsDelegate)
							{
								if (PascalOrBasic(n))
								{
									ConvertCallToIndexAccess(a, pos);
									return;
								}

								// 'type' does not contain a definition for 'function'
								scripter.CreateErrorObjectEx(Errors.CS0117, c.Name, method_name);
								return;
							}

							if (d.ImportedType != null)
							{
								int level = symbol_table[r.arg1].Level;
								int level_type_id = symbol_table[level].TypeId;
								ClassObject cc = GetClassObject(level_type_id);
								if (cc.ImportedType != null)
								{
									r.op = OP_DYNAMIC_INVOKE;
									return;
								}
							}

							sub_id = d.PatternMethod.Id;
							if (PascalOrBasic(n))
							{
								int z = n;
								bool b = false;
								for (;;)
								{
									z--;
									if (this[z].op == OP_RAISE_EVENT && this[z].arg1 == r.arg1)
									{
										b = true;
										break;
									}

									if (z == 1) break;
								}

								if (b)
									this[n-1].arg1 = prev_arg1;
								else
									this[n-1].arg1 = r.arg1;
							}
							else
								this[n-1].arg1 = prev_arg1; // as we have to pass the delegate instance
						 }
						 else
						 {
								// 'type' does not contain a definition for 'function'
								scripter.CreateErrorObjectEx(Errors.CS0117, c.Name, method_name);
								return;
						 }
					}
				}
			} // Kind.Ref
			else if ((k == MemberKind.Var) || (k == MemberKind.Index))
			{
				int type_id = symbol_table[r.arg1].TypeId;
				ClassObject d = GetClassObject(type_id);

				if (k == MemberKind.Var && (d.IsArray || d.IsPascalArray) && PascalOrBasic(n))
				{
					ConvertCallToIndexAccess(a, pos);
					return;
				}

				if (k == MemberKind.Var && d.DefaultProperty != null && PascalOrBasic(n))
				{
					ConvertToDefaultPropertyCall(d.DefaultProperty, a, pos);
					return;
				}

				if (k == MemberKind.Property && PascalOrBasic(n))
				{
					ConvertToPropertyCall(a, pos);
					return;
				}

				// is it a delegate call?

				if (!d.IsDelegate)
				{
					if (PascalOrBasic(n))
					{
						ConvertCallToIndexAccess(a, pos);
						return;
					}

					// 'construct1_name' denotes a 'construct1' where a 'construct2' was expected
					string s = symbol_table[r.arg1].Name;
					scripter.CreateErrorObjectEx(Errors.CS0118, s, "variable", "method");
					return;
				}
				if (PascalOrBasic(n))
					this[n - 1].arg1 = r.arg1;

				sub_id = d.PatternMethod.Id;
			}
			else
			{
				int type_id = symbol_table[r.arg1].TypeId;
				ClassObject d = GetClassObject(type_id);

				if (k == MemberKind.Field && (d.IsArray || d.IsPascalArray) && PascalOrBasic(n))
				{
					ConvertCallToIndexAccess(a, pos);
					return;
				}

				if (k == MemberKind.Field && d.DefaultProperty != null && PascalOrBasic(n))
				{
					ConvertToDefaultPropertyCall(d.DefaultProperty, a, pos);
					return;
				}

				if (k == MemberKind.Property && PascalOrBasic(n))
				{
					ConvertToPropertyCall(a, pos);
					return;
				}

				if (!d.IsDelegate)
				{
					string s = symbol_table[r.arg1].Name;
					scripter.CreateErrorObjectEx(Errors.CS0118, s, "variable", "method");
					return;
				}

				if (k == MemberKind.Field && d.ImportedType != null)
				{
					int level = symbol_table[r.arg1].Level;
					if (symbol_table[level].Kind == MemberKind.Type)
					{
						ClassObject cc = GetClassObject(level);
						if (cc.ImportedType != null)
						{
							r.op = OP_DYNAMIC_INVOKE;
							return;
						}
					}
				}

				this[n - 1].arg1 = r.arg1;
				sub_id = d.PatternMethod.Id;
			}

			process:

			r.arg1 = sub_id;
			for (j = 0; j < pos.Count; j++)
				this[pos[j]].res = sub_id;

			int res_type_id = symbol_table[r.arg1].TypeId;
			if (res_type_id == (int) StandardType.Void)
				r.res = 0;
			else if (symbol_table[r.arg1].Name != "get_Current")
				symbol_table[r.res].TypeId = res_type_id;

			// check parameter passing

			bool is_error = false;
			string s1 = "";
			string s2 = "";

			FunctionObject f = GetFunctionObject(sub_id);

			if (f.Static)
			{
				int type_id = symbol_table[sub_id].Level;
				this[n_call - 1].arg1 = type_id;
			}

			if ((!f.Imported) && (a.Count > 0))
				for (j = 0; j < f.ParamCount; j++)
				{
					ParamMod pm1 = f.GetParamMod(j);
					ParamMod pm2 = (ParamMod) param_mod[j];
					if (pm1 != pm2)
					{
						int id1 = f.GetParamId(j);
						int id2 = this[n].arg1;
						int t1 = symbol_table[id1].TypeId;
						int t2 = symbol_table[id2].TypeId;
						s1 = symbol_table[t1].Name;
						s2 = symbol_table[t2].Name;
						if (pm1 == ParamMod.RetVal)
							s1 = "ref " + s1;
						else if (pm1 == ParamMod.Out)
							s1 = "out " + s1;
						if (pm2 == ParamMod.RetVal)
							s2 = "ref " + s2;
						else if (pm2 == ParamMod.Out)
							s2 = "out " + s2;
						is_error = true;

						if (is_error && PascalOrBasic(n))
						{
							is_error = false;
							if (pm1 == ParamMod.RetVal)
							{
								param_mod[j] = (int) pm1;
								retValCount ++;
							}
						}
						else
						{
							n = pos[j];
							break;
						}
					}
				}

			if (is_error)
			{
				// Argument 'arg': cannot convert from 'type1' to 'type2'
				scripter.CreateErrorObjectEx(Errors.CS1503, j+1, s2, s1);
				return;
			}

			if ((retValCount > 0) && (a.Count > 0))
			{
				InsertOperators(n + 1, retValCount * 2);
				for (j = 0; j < f.ParamCount; j++)
				{
					int param_id = f.GetParamId(j);
					ParamMod pm = (ParamMod) param_mod[j];
					if (pm != ParamMod.None)
					{
						int res_id = AppVar(symbol_table[sub_id].Level, symbol_table[param_id].TypeId);

						n++;
						this[n].op = OP_GET_PARAM_VALUE;
						this[n].arg1 = sub_id;
						this[n].arg2 = j;
						this[n].res = res_id;

						n++;
						this[n].op = OP_ASSIGN;
						this[n].arg1 = a[j];
						this[n].arg2 = res_id;
						this[n].res = a[j];
					}
				}
			}
		} // OP_CALL

		/// <summary>
		/// Convertes OP_CALL instruction set to OP_CREATE_INDEX instruction set
		/// (VB.NET)
		/// </summary>
		void ConvertCallToIndexAccess(IntegerList a, IntegerList pos)
		{
			int array_id = r.arg1;
			int index_object_id = r.res;

			symbol_table[index_object_id].Level = array_id;
			symbol_table[index_object_id].Kind = MemberKind.Index;

			int z = pos[0];
			for (;;)
			{
				z--;
				if (this[z].op == OP_NOP) break;
			}
			int temp = z;
			this[z].op = OP_CREATE_INDEX_OBJECT;
			this[z].arg1 = array_id;
			this[z].arg2 = 0;
			this[z].res = index_object_id;

			for (int v = 0; v < pos.Count; v++)
			{
				z = pos[v];
				this[z].op = OP_ADD_INDEX;
				this[z].arg1 = index_object_id;
				this[z].arg2 = a[v];
				this[z].res = 0;
			}
			z = n - 1;
			this[z].op = OP_NOP;
			this[z].arg1 = 0;
			this[z].arg2 = 0;
			this[z].res = 0;

			z = n;
			this[z].op = OP_SETUP_INDEX_OBJECT;
			this[z].arg1 = index_object_id;
			this[z].arg2 = 0;
			this[z].res = 0;
			n = temp - 1;
		}

		/// <summary>
		/// Convertes OP_CALL instruction set to default property call instruction set
		/// (VB.NET)
		/// </summary>
		void ConvertToDefaultPropertyCall(PropertyObject p, IntegerList a, IntegerList pos)
		{
			int object_id = r.arg1;

			int write_n = 0;
			int j;

			// determine n_write

			if (this[n + 1].op == OP_ASSIGN && this[n + 1].arg1 == this[n].res)
			{
				write_n = n + 1;
			}

			FunctionObject f;
			if (write_n > 0)
			{
				if (p.WriteId == 0)
				{
					// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
					scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
					return;
				}
				f = GetFunctionObject(p.WriteId);

				// convert assignment

				int val_id = this[write_n].arg2;

				r.arg1 = p.WriteId;
				this[n - 1].arg1 = object_id;

				r.arg2 ++; // increase passed param number

				if (f.ParamCount != r.arg2)
				{
					// No overload for method 'method' takes 'number' arguments
					scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, r.arg2);
				}
				for (j = 0; j < pos.Count; j++)
				{
					this[pos[j]].res = p.WriteId;
				}

				// insert value
				InsertOperators(n - 1, 1);

				this[n - 1].op = OP_PUSH;
				this[n - 1].arg1 = val_id;
				this[n - 1].arg2 = 0;
				this[n - 1].res = p.WriteId;

				n++;
				this[n + 1].op = OP_NOP;

				return;
			}

			if (p.ReadId == 0)
			{
				// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
				scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
				return;
			}

			r.arg1 = p.ReadId;

			if (r.res >= 0)
				symbol_table[r.res].TypeId = symbol_table[r.arg1].TypeId;

			this[n - 1].arg1 = object_id;
			for (j = 0; j < pos.Count; j++)
			{
				this[pos[j]].res = p.ReadId;
			}

			f = GetFunctionObject(p.ReadId);
			if (f.ParamCount != r.arg2)
			{
				// No overload for method 'method' takes 'number' arguments
				scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, r.arg2);
			}
		}

		/// <summary>
		/// Convertes OP_CALL instruction set to property call instruction set
		/// (VB.NET)
		/// </summary>
		void ConvertToPropertyCall(IntegerList a, IntegerList pos)
		{
			PropertyObject p = (PropertyObject) symbol_table[r.arg1].Val;
			int object_id;

			if (p.Static)
				object_id = symbol_table[r.arg1].TypeId;
			else
				object_id = r.arg1;

			int write_n = 0;
			int j;

			// determine n_write

			if (this[n + 1].op == OP_ASSIGN && this[n + 1].arg1 == this[n].res)
			{
				write_n = n + 1;
			}

			FunctionObject f;
			if (write_n > 0)
			{
				if (p.WriteId == 0)
				{
					// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
					scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
					return;
				}
				f = GetFunctionObject(p.WriteId);

				// convert assignment

				int val_id = this[write_n].arg2;

				r.arg1 = p.WriteId;
				this[n - 1].arg1 = object_id;

				r.arg2 ++; // increase passed param number

				if (f.ParamCount != r.arg2)
				{
					// No overload for method 'method' takes 'number' arguments
					scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, r.arg2);
				}
				for (j = 0; j < pos.Count; j++)
				{
					this[pos[j]].res = p.WriteId;
				}

				// insert value
				InsertOperators(n - 1, 1);

				this[n - 1].op = OP_PUSH;
				this[n - 1].arg1 = val_id;
				this[n - 1].arg2 = 0;
				this[n - 1].res = p.WriteId;

				n++;
				this[n + 1].op = OP_NOP;

				return;
			}

			if (p.ReadId == 0)
			{
				// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
				scripter.CreateErrorObjectEx(Errors.CS0154, p.Name);
				return;
			}

			r.arg1 = p.ReadId;
			this[n - 1].arg1 = object_id;
			for (j = 0; j < pos.Count; j++)
			{
				this[pos[j]].res = p.ReadId;
			}

			f = GetFunctionObject(p.ReadId);
			if (f.ParamCount != r.arg2)
			{
				// No overload for method 'method' takes 'number' arguments
				scripter.CreateErrorObjectEx(Errors.CS1501, f.FullName, r.arg2);
			}
		}

		/// <summary>
		/// Performs type checking, replaces "general" operators with appropriate
		/// "detailed" operators, resolves overloaded method calls etc.
		/// </summary>
		public void CheckTypes()
		{
			CheckTypesEx(0, 0, null);
		}

		/// <summary>
		/// Performs type checking, replaces "general" operators with appropriate
		/// "detailed" operators, resolves overloaded method calls etc.
		/// </summary>
		public void CheckTypesEx(int init_n, int init_level,
			IntegerStack init_class_stack)
		{
			if (scripter.IsError()) return;

			IntegerList a = new IntegerList(true);
			IntegerList param_mod = new IntegerList(true);
			IntegerList pos = new IntegerList(true);
			IntegerList global_list = new IntegerList(false);
			IntegerList l = new IntegerList(false);

			ClassObject enum_class = null;

			ClassObject current_class;
			IntegerStack class_stack;
			if (init_class_stack == null)
				class_stack = new IntegerStack();
			else
				class_stack = init_class_stack.Clone();
			if (class_stack.Count == 0)
				current_class = null;
			else
				current_class = GetClassObject(class_stack.Peek());

			n = init_n;
			int curr_level = init_level;
			for (;;)
			{
next_iter:
				n++;

				if (n >= Card) break;

				r = (ProgRec) prog[n];
				int op = r.op;

				if (op == OP_SEPARATOR || op == OP_LABEL || op == OP_NOP)
				{
					continue;
				}
				else if (op == OP_CREATE_INDEX_OBJECT)
				{
					int array_type_id = symbol_table[r.arg1].TypeId;
					string array_type_name = symbol_table[array_type_id].Name;
					string element_type_name = PaxSystem.GetElementTypeName(array_type_name);

					int element_type_id = 0;
					PropertyObject p = null;

					ClassObject c;
					if (element_type_name == "")
					{
						c = GetClassObject(array_type_id);
						if (c.IsPascalArray)
						{
							array_type_name = c.ImportedType.Name + "[]";
//							element_type_name = PaxSystem.GetElementTypeName(array_type_name);

							element_type_name = symbol_table[c.IndexTypeId].Name;
						}
					}

					if (element_type_name == "")
					{
						// is it an indexer?
						c = GetClassObject(array_type_id);
						p = c.FindIndexer();
						if (p == null)
						{
							// cannot apply indexing
							scripter.CreateErrorObjectEx(Errors.CS0021, array_type_name);
							break;
						}
						element_type_id = symbol_table[p.Id].TypeId;
					}
					else
					{
						for (int j=array_type_id; j >= 0; j--)
						{
							if (symbol_table[j].Kind == MemberKind.Type)
								if (symbol_table[j].Name == element_type_name)
								{
									element_type_id = j;
									break;
								}
						}
					}
					if (element_type_id == 0)
					{
						array_type_name = symbol_table[array_type_id].FullName;
						element_type_name = PaxSystem.GetElementTypeName(array_type_name);

						bool upcase = GetUpcase();

						MemberObject m = FindType(RecreateLevelStack(n), element_type_name, upcase);
						if (m == null)
						{
							// The type or namespace name 'type/namespace' could not be found (are you missing a using directive or an assembly reference?)
							scripter.CreateErrorObjectEx(Errors.CS0246, element_type_name);
							break;
						}
						else
							element_type_id = m.Id;

					}
					symbol_table[r.res].TypeId = element_type_id;
				}
				else if (op == OP_ADD_INDEX)
				{
					int type_id = symbol_table[r.res].TypeId;
					if (type_id == (int) StandardType.String)
					{
						if ((this[n+1].op == OP_ADD_INDEX) &&
							(this[n+1].res == r.res))
						{
							int k = 1;
							while ((this[n+k].op == OP_ADD_INDEX) &&
									(this[n+k].res == r.res))
									k++;
							// No overload for method 'method' takes 'number' arguments
							scripter.CreateErrorObjectEx(Errors.CS1501, "this", k);
							break;
						}
					}
				}
				else if (op == OP_SETUP_INDEX_OBJECT)
				{
					int k = n;
					int add_index_count = 0;
					for (;;)
					{
						k--;
						if ((this[k].op == OP_ADD_INDEX) && (this[k].arg1 == r.arg1))
							add_index_count++;
						if ((this[k].op == OP_CREATE_INDEX_OBJECT) && (this[k].res == r.arg1))
							break;
					}
					int array_type_id = symbol_table[this[k].arg1].TypeId;
					string array_type_name = symbol_table[array_type_id].Name;
					int rank = PaxSystem.GetRank(array_type_name);
					if (rank > 0)
					{
						if (rank != add_index_count)
						{
							// Wrong number of indices inside []
							scripter.CreateErrorObjectEx(Errors.CS0022, rank.ToString());
							break;
						}
					}
				}

				if ((op == OP_CREATE_INDEX_OBJECT) || (op == OP_ADD_INDEX) ||
					(op == OP_SETUP_INDEX_OBJECT))
						continue;

				for (int k = 1; k <= 2; k++)
				{
					int id;

					if (k == 1)
					  id = r.arg1;
					else
					  id = r.arg2;

					if (symbol_table[id].Kind == MemberKind.Index)
					{
						// create list of replaced instructions
						l.Clear();
						// find OP_CREATE_INDEX_OBJECT
						int j = n;
						for (;;)
						{
							if ((this[j].op == OP_CREATE_INDEX_OBJECT) && (this[j].res == id))
								break;
							else
								j--;
						}

						int object_id = this[j].arg1;
						l.Add(j); // add this instruction to l list
						int type_id = symbol_table[object_id].TypeId;

						ClassObject c = GetClassObject(type_id);
						PropertyObject p = c.FindIndexer();
						if (p != null)
						{
							// find OP_SETUP_INDEX_OBJECT
							for (;;)
							{
								if ((this[j].op == OP_SETUP_INDEX_OBJECT) && (this[j].arg1 == id))
									break;
								else
								{
									if ((this[j].op == OP_ADD_INDEX) && (this[j].arg1 == id))
										l.Add(j); // add this instruction to l list
									j++;
								}
							}
							l.Add(j); // add this instruction to l list
							if ((r.op == OP_ASSIGN) && (k == 1))
							{
								if (p.WriteId == 0)
								{
									// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
									scripter.CreateErrorObject(Errors.CS0154);
									break;
								}

								int val_id = r.arg2;

								InsertOperators(n, p.ParamCount + 3);
								n--;

								n++;
								this[n].op = OP_BEGIN_CALL;
								this[n].arg1 = p.WriteId;
								this[n].arg2 = 0;
								this[n].res = 0;

								for (int m = 1; m < l.Count - 1; m++)
								{
									n++;
									this[n].op = OP_PUSH;
									this[n].arg1 = this[l[m]].arg2;
									this[n].arg2 = 0;
									this[n].res = p.WriteId;
								}

								n++;
								this[n].op = OP_PUSH;
								this[n].arg1 = val_id;
								this[n].arg2 = 0;
								this[n].res = p.WriteId;

								n++;
								this[n].op = OP_PUSH;
								this[n].arg1 = object_id;
								this[n].arg2 = 0;
								this[n].res = 0;

								n++;
								this[n].op = OP_CALL_BASE;
								this[n].arg1 = p.WriteId;
								this[n].arg2 = l.Count - 1;
								this[n].res = 0;

								r = (ProgRec) prog[n];
								op = r.op;
							}
							else
							{
								if (p.ReadId == 0)
								{
									// The property or indexer 'property' cannot be used in this context because it lacks the get accessor
									scripter.CreateErrorObject(Errors.CS0154);
									break;
								}

								// create result variable
								int res_id = AppVar(symbol_table[id].Level, symbol_table[id].TypeId);

								// replace source id

                                if (k == 1)
                                {
                                    for (int jj = r.arg1; jj <= symbol_table.Card; jj++)
                                        if (symbol_table[jj].Kind == MemberKind.Ref)
                                            if (symbol_table[jj].Level == r.arg1)
                                            {
                                                symbol_table[jj].Level = res_id;
                                                break;
                                            }
                                    r.arg1 = res_id;
                                }
                                else
                                {
                                    for (int jj = r.arg2; jj <= symbol_table.Card; jj++)
                                        if (symbol_table[jj].Kind == MemberKind.Ref)
                                            if (symbol_table[jj].Level == r.arg2)
                                            {
                                                symbol_table[jj].Level = res_id;
                                                break;
                                            }
                                    r.arg2 = res_id;
                                }

								InsertOperators(n, p.ParamCount + 3);
								n--;

								n++;
								this[n].op = OP_BEGIN_CALL;
								this[n].arg1 = p.ReadId;
								this[n].arg2 = 0;
								this[n].res = 0;

								for (int m = 1; m < l.Count - 1; m++)
								{
									n++;
									this[n].op = OP_PUSH;
									this[n].arg1 = this[l[m]].arg2;
									this[n].arg2 = 0;
									this[n].res = p.ReadId;
								}

								n++;
								this[n].op = OP_PUSH;
								this[n].arg1 = object_id;
								this[n].arg2 = 0;
								this[n].res = 0;

								n++;
								this[n].op = OP_CALL_BASE;
								this[n].arg1 = p.ReadId;
								this[n].arg2 = l.Count - 2;
								this[n].res = res_id;
								symbol_table[this[n].res].TypeId = symbol_table[p.ReadId].TypeId;

								r = (ProgRec) prog[n];
								op = r.op;

							}
							global_list.AddFrom(l);
						}
					}
				} // k-loop

				if (op == OP_CREATE_METHOD)
				  curr_level = r.arg1;
				else if (op == OP_CALL_SIMPLE)
				{
					r.op = OP_CALL;
					int sub_id = r.arg1;
					MemberKind k = symbol_table[sub_id].Kind;
					if (k == MemberKind.Method)
					{
						int param_count = r.arg2;

						// find actual parameter list
						a.Clear();
						param_mod.Clear();
						pos.Clear();

						if (param_count > 0)
						{
							int j = n - 1;
							for (;;)
							{
								if ((this[j].op == OP_PUSH) && (this[j].res == sub_id))
								{
									pos.Add(j);
									a.Add(this[j].arg1);
									param_mod.Add(this[j].arg2);

									if (a.Count == param_count)
										break;
								}
								j--;
								if (j == 0)
								{
									// Internal compiler error
									scripter.CreateErrorObject(Errors.CS0001);
									return;
								}
							}
						}

						FunctionObject f = GetFunctionObject(sub_id);

						bool ok = true;
						for (int j = 0; j < param_count; j++)
						{
							int formal_id = f.GetParamId(j);
							int actual_id = a[j];

							if (!f.Imported)
								ok = (int)f.GetParamMod(j) == param_mod[j];
							else
								ok = true;
							if (!ok)
							{
								n = pos[j];
								ParamMod pm1 = f.GetParamMod(j);
								ParamMod pm2 = (ParamMod) param_mod[j];
								int t1 = symbol_table[formal_id].TypeId;
								int t2 = symbol_table[actual_id].TypeId;
								string s1 = symbol_table[t1].Name;
								string s2 = symbol_table[t2].Name;
								if (pm1 == ParamMod.RetVal)
									s1 = "ref " + s1;
								else if (pm1 == ParamMod.Out)
									s1 = "out " + s1;
								if (pm2 == ParamMod.RetVal)
									s2 = "ref " + s2;
								else if (pm2 == ParamMod.Out)
									s2 = "out " + s2;
								// Cannot impllicitly convert type
								scripter.CreateErrorObjectEx(Errors.CS0029, s1, s2);
								break;
							}
							ok = scripter.MatchAssignment(formal_id, actual_id);
							if (!ok)
							{
								n = pos[j];
								int t1 = symbol_table[formal_id].TypeId;
								int t2 = symbol_table[actual_id].TypeId;
								ClassObject c1 = GetClassObject(t1);
								ClassObject c2 = GetClassObject(t2);

								int conversion_id = c1.FindOverloadableImplicitOperatorId(actual_id, formal_id);

								if (conversion_id > 0)
								{
									int curr_sub_id = GetCurrMethodId();
									int res_id = AppVar(curr_sub_id, t1);

									int dest_sub_id = this[n].res;

									this[n].arg1 = res_id;

									InsertOperators(n, 3);

									this[n].op = OP_PUSH;
									this[n].arg1 = actual_id;
									this[n].arg2 = 0;
									this[n].res = conversion_id;

									n++;
									this[n].op = OP_PUSH;
									this[n].arg1 = c1.Id;
									this[n].arg2 = 0;
									this[n].res = 0;

									n++;
									this[n].op = OP_CALL_SIMPLE;
									this[n].arg1 = conversion_id;
									this[n].arg2 = 1;
									this[n].res = res_id;


									for (;;)
									{
										if (this[n].arg1 == dest_sub_id) // OP_CALL || OP_CALL_SIMPLE
											break;
										n++;
									}

									goto next_iter;

								}
								else
								{
									// Cannot impllicitly convert type
									scripter.CreateErrorObjectEx(Errors.CS0029, c2.Name, c1.Name);
									break;
								}
							}
						}
						if (!ok)
							break;
					}
				}
				else if ((op == OP_CALL) || (op == OP_CALL_BASE))
				{
					CheckOP_CALL(current_class,
						  curr_level,
						  a,
						  param_mod,
						  pos);
					if (scripter.IsError())
						break;
				} // OP_CALL
				else if (op == OP_CHECK_STRUCT_CONSTRUCTOR)
				{
					// arg1 - type_id
					// res - id
					ClassObject c = GetClassObject(r.arg1);
					if ((c.IsStruct) && (!c.Imported))
					{
						r.op = OP_CREATE_OBJECT;

						FunctionObject best;
						int sub_id = c.FindConstructorId(null, null, out best);

						if (sub_id == 0)
						{
							// The type 'class' has no constructors defined
							scripter.CreateErrorObjectEx(Errors.CS0143, c.Name);
						}
						n++;
						InsertOperators(n, 2);

						this[n].op = OP_PUSH;
						this[n].arg1 = r.res;
						this[n].arg2 = 0;
						this[n].res = 0;

						n++;

						this[n].op = OP_CALL;
						this[n].arg1 = sub_id;
						this[n].arg2 = 0;
						this[n].res = 0;
					}
					else
						r.op = OP_NOP;
				}
				else if (op == OP_INSERT_STRUCT_CONSTRUCTORS) // Pascal only
				{
					// arg1 - type_id
					ClassObject c = GetClassObject(r.arg1);
					for (int i = 0; i < c.Members.Count; i++)
					{
						MemberObject m = c.Members[i];
						if (m.Kind == MemberKind.Field)
						{
							int m_type_id = symbol_table[m.Id].TypeId;
							ClassObject mc = GetClassObject(m_type_id);
							if ((mc.IsStruct) && (!mc.Imported))
							{
								if (mc.IsPascalArray)
								{
									n++;
									InsertOperators(n, 1);

									this[n].op = OP_CREATE_OBJECT;
									this[n].arg1 = m_type_id;
									this[n].arg2 = 0;
									this[n].res = m.Id;
								}
								else
								{
									FunctionObject best;
									int sub_id = mc.FindConstructorId(null, null, out best);

									if (sub_id == 0)
									{
										// The type 'class' has no constructors defined
										scripter.CreateErrorObjectEx(Errors.CS0143, mc.Name);
									}

									n++;
									InsertOperators(n, 3);

									this[n].op = OP_CREATE_OBJECT;
									this[n].arg1 = m_type_id;
									this[n].arg2 = 0;
									this[n].res = m.Id;

									n++;
									this[n].op = OP_PUSH;
									this[n].arg1 = m.Id;
									this[n].arg2 = 0;
									this[n].res = 0;

									n++;
									this[n].op = OP_CALL;
									this[n].arg1 = sub_id;
									this[n].arg2 = 0;
									this[n].res = 0;
								}
							}
						}
					}
				}
				else if (op == OP_CAST)
				{
					CheckOP_CAST();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_TO_SBYTE) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Sbyte;
				}
				else if (op == OP_TO_BYTE) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Byte;
				}
				else if (op == OP_TO_USHORT) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Ushort;
				}
				else if (op == OP_TO_SHORT) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Short;
				}
				else if (op == OP_TO_UINT) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Uint;
				}
				else if (op == OP_TO_INT) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Int;
				}
				else if (op == OP_TO_ULONG) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Ulong;
				}
				else if (op == OP_TO_LONG) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Long;
				}
				else if (op == OP_TO_CHAR) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Char;
				}
				else if (op == OP_TO_FLOAT) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Float;
				}
				else if (op == OP_TO_DOUBLE) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Double;
				}
				else if (op == OP_TO_DECIMAL) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Decimal;
				}
				else if (op == OP_TO_STRING) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.String;
				}
				else if (op == OP_TO_BOOLEAN) // VB.NET
				{
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else if (op == OP_INC)
				{
					CheckOP_INC();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_DEC)
				{
					CheckOP_DEC();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_ASSIGN_COND_TYPE)
				{
					CheckOP_ASSIGN_COND_TYPE();
				}
				else if (op == OP_ADD_EXPLICIT_INTERFACE)
				{
					OperAddExplicitInterface();
				}
				else if (op == OP_END_CLASS)
				{
					class_stack.Pop();

					if (class_stack.Count > 0)
					{
						int class_id = class_stack.Peek();
						current_class = GetClassObject(class_id);
					}
					else
						current_class = null;
				}
				else if (op == OP_CREATE_CLASS)
				{
					bool upcase = GetUpcase(n);

					ClassObject c = GetClassObject(r.arg1);
					if (c.Class_Kind == ClassKind.Enum)
						enum_class = c;

					current_class = c;
					class_stack.Push(current_class.Id);

					if  (!(c.IsClass || c.IsStruct))
						continue;

					int non_interface_count = 0;
					for (int j = 0; j < c.AncestorIds.Count; j++)
					{
						int id = c.AncestorIds[j];
						ClassObject t = GetClassObject(id);
						if (!t.IsInterface)
						{
							non_interface_count++;
							if (non_interface_count > 1)
							{
								scripter.CreateErrorObjectEx(Errors.CS0527, t.Name);
								break;
							}
						}
					}

					IntegerList ls = c.GetSupportedInterfaceListIds();

					if (c.AncestorClass.HasModifier(Modifier.Abstract))
						ls.Add(c.AncestorClass.Id);

					for (int j = 0; j < ls.Count; j++)
					{
						int id = ls[j];
						ClassObject t = GetClassObject(id);

						for (int k = 0; k < t.Members.Count; k++)
						{
							MemberObject mk = t.Members[k];

							if (!mk.HasModifier(Modifier.Abstract))
								continue;

							if ((mk.Kind == MemberKind.Method) ||
								(mk.Kind == MemberKind.Constructor) ||
								(mk.Kind == MemberKind.Destructor)
								)
							{
								bool ok = false;
								FunctionObject fk = (FunctionObject) mk;
								for (int x = 0; x < c.Members.Count; x++)
								{
									MemberObject mx = c.Members[x];
									if (!mx.Public)
										continue;

									if (mk.Kind == mx.Kind)
									{
										if (PaxSystem.CompareStrings(mk.Name, mx.Name, upcase))
										{
											FunctionObject fx = (FunctionObject) mx;
											if (FunctionObject.CompareHeaders(fx, fk))
											{
												ok = true;
												break;
											}
										}

										if (mx.ImplementsId != 0)
										{
											string impl_name = symbol_table[mx.ImplementsId].Name;
											if (PaxSystem.CompareStrings(mk.Name, impl_name, upcase))
											{
												ok = true;
												break;
											}
										}
									}
								}
								if (!ok)
								{
									if (t.IsInterface)
										// 'class' does not implement interface member 'member'
										scripter.CreateErrorObjectEx(Errors.CS0535, c.FullName, t.FullName + "." + fk.Name);
									else if (t.IsClass)
										// 'function1' does not implement inherited abstract member 'function2'
										scripter.CreateErrorObjectEx(Errors.CS0534, c.FullName, t.FullName + "." + fk.Name);
									break;
								}
							}
							else if (mk.Kind == MemberKind.Property)
							{
								bool ok = false;
								PropertyObject pk = (PropertyObject) mk;
								for (int x = 0; x < c.Members.Count; x++)
								{
									MemberObject mx = c.Members[x];

									if (!mx.Public)
										continue;

									if (mk.Kind == mx.Kind)
									{
										if (PaxSystem.CompareStrings(mk.Name, mx.Name, upcase))
										{
											ok = true;
											break;
										}
										if (mx.ImplementsId != 0)
										{
											string impl_name = symbol_table[mx.ImplementsId].Name;
											if (PaxSystem.CompareStrings(mk.Name, impl_name, upcase))
											{
												ok = true;
												break;
											}
										}
									}
								}
								if (!ok)
								{
									if (t.IsInterface)
										// 'class' does not implement interface member 'member'
										scripter.CreateErrorObjectEx(Errors.CS0535, c.FullName, t.FullName + "." + pk.Name);
									else if (t.IsClass)
										// 'function1' does not implement inherited abstract member 'function2'
										scripter.CreateErrorObjectEx(Errors.CS0534, c.FullName, t.FullName + "." + pk.Name);
									break;
								}
							}
						}
					}
				}
				else if (op == OP_CREATE_OBJECT)
				{
					ClassObject c = GetClassObject(r.arg1);
					if (c.Abstract || c.IsInterface)
					{
						// Cannot create an instance of the abstract class or interface 'interface'
						scripter.CreateErrorObjectEx(Errors.CS0144, c.Name);
						break;
					}
				}
				else if (op == OP_END_USING)
				{
					if (enum_class != null)
						if (enum_class.Id == r.arg1)
							enum_class = null;
				}
				else if (op == OP_ASSIGN)
				{
					CheckOP_ASSIGN(enum_class);
					if (scripter.IsError())
						break;
				}
				else if (op == OP_UNARY_MINUS)
				{
					if (!SetupDetailedUnaryOperator(op, "-", detailed_negation_operators))
						break;
					if (this[n].op != OP_CALL_SIMPLE)
					{
						r = this[n];
						int t1 = GetTypeId(r.arg1);
						int tr = GetTypeId(r.res);
						if (t1 != tr && IsNumericTypeId(tr))
							InsertNumericConversion(tr, 1);
					}
				}
				else if (op == OP_NOT)
				{
					if (!SetupDetailedUnaryOperator(op, "!", detailed_logical_negation_operators))
						break;
				}
				else if (op == OP_COMPLEMENT)
				{
					if (!SetupDetailedUnaryOperator(op, "~", detailed_bitwise_complement_operators))
						break;
					if (this[n].op != OP_CALL_SIMPLE)
					{
						r = this[n];
						int t1 = GetTypeId(r.arg1);
						int tr = GetTypeId(r.res);
						if (t1 != tr && IsNumericTypeId(tr))
							InsertNumericConversion(tr, 1);
					}
				}
				else if (op == OP_PLUS)
				{
					CheckOP_PLUS();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_MINUS)
				{
					CheckOP_MINUS();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_MULT)
				{
					CheckOP_MULT();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_EXPONENT)
				{
					CheckOP_EXP();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_DIV)
				{
					CheckOP_DIV();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_MOD)
				{
					CheckOP_MOD();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_LEFT_SHIFT)
				{
					CheckOP_LEFT_SHIFT();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_RIGHT_SHIFT)
				{
					CheckOP_RIGHT_SHIFT();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_BITWISE_AND)
				{
					CheckOP_BITWISE_AND();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_BITWISE_OR)
				{
					CheckOP_BITWISE_OR();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_BITWISE_XOR)
				{
					CheckOP_BITWISE_XOR();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_LOGICAL_AND)
				{
					CheckOP_LOGICAL_AND();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_LOGICAL_OR)
				{
					CheckOP_LOGICAL_OR();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_LT)
				{
					if (!SetupDetailedBinaryOperator(op, "<", detailed_lt_operators))
						break;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else if (op == OP_LE)
				{
					if (!SetupDetailedBinaryOperator(op, "<=", detailed_le_operators))
						break;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else if (op == OP_GT)
				{
					if (!SetupDetailedBinaryOperator(op, ">", detailed_gt_operators))
						break;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else if (op == OP_GE)
				{
					if (!SetupDetailedBinaryOperator(op, ">=", detailed_ge_operators))
						break;
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else if (op == OP_EQ)
				{
					CheckOP_EQ();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_NE)
				{
					CheckOP_NE();
					if (scripter.IsError())
						break;
				}
				else if (op == OP_IS)
				{
					symbol_table[r.res].TypeId = (int) StandardType.Bool;
				}
				else if (op == OP_AS)
				{
					if (symbol_table[r.arg2].Kind != MemberKind.Type)
					{
						// type expected
						scripter.CreateErrorObject(Errors.CS1031);
						break;
					}
					ClassObject c1 = GetClassObject(symbol_table[r.arg1].TypeId);
					ClassObject c2 = GetClassObject(r.arg2);
					if (c2.IsValueType)
					{
						// The as operator must be used with a reference type
						scripter.CreateErrorObjectEx(Errors.CS0077, c2.Name);
						break;
					}
					if (!scripter.conversion.ExistsImplicitReferenceConversion(c2, c1))
					{
						// Cannot convert type
						scripter.CreateErrorObjectEx(Errors.CS0039, c2.Name, c1.Name);
						break;
					}
					symbol_table[r.res].TypeId = r.arg2;
				}
				else if (op == OP_CREATE_REFERENCE)
				{
					CheckOP_CREATE_REFERENCE(current_class);
                    if (scripter.IsError())
						break;
				}
				else if (op == OP_DECLARE_LOCAL_VARIABLE)
					OperDeclareLocalVariable();
				else if (op == OP_ADDRESS_OF) // VB.NET
				{
					ProcessAddressOf();
					if (scripter.IsError())
						break;
				}
			}

			// remove old instructions

			for (int i = 0; i < global_list.Count; i++)
			{
				n = global_list[i];
				this[n].op = OP_NOP;
			}
		}

		/// <summary>
		/// Inserts virtual calls and OP_ADD_EVENT operators.
		/// </summary>
		public void AdjustCalls()
		{
			AdjustCallsEx(0);
		}

		/// <summary>
		/// Inserts virtual calls and OP_ADD_EVENT operators.
		/// </summary>
		public void AdjustCallsEx(int init_n)
		{
			if (scripter.IsError()) return;

			for (int i = init_n + 1; i <= symbol_table.Card; i++)
			{
				MemberKind k = symbol_table[i].Kind;
				if ((k == MemberKind.Method) ||
					(k == MemberKind.Constructor) ||
					(k == MemberKind.Destructor))
				{
					FunctionObject f = GetFunctionObject(i);
					f.CreateSignature();
				}
			}

			n = init_n;

			for (;;)
			{
				n++;
				if (n >= Card) break;

				if (this[n].op == OP_CALL_SIMPLE)
					this[n].op = OP_CALL;

				if (this[n].op == OP_CALL)
				{
					int sub_id = this[n].arg1;
					if (symbol_table[sub_id].Kind == MemberKind.Method)
					{
						FunctionObject f = GetFunctionObject(sub_id);

						if ((f.HasModifier(Modifier.Virtual) ||
							f.HasModifier(Modifier.Override) ||
							f.HasModifier(Modifier.Abstract)))
						{
							this[n].op = OP_CALL_VIRT;
						}

						if (f.ParamCount == 1)
						{
							if (f.Imported && f.Name.Length > 4 && f.Name.Substring(0, 4) == "add_")
							{
								int param_type_id = symbol_table[f.Param_Ids[0]].TypeId;
								ClassObject paramClass = GetClassObject(param_type_id);
								if (paramClass.IsDelegate)
								{
									string event_name = f.Name.Substring(4);
									EventInfo e = f.Owner.ImportedType.GetEvent(event_name);
									FunctionObject p = paramClass.PatternMethod;
									if ((e != null) && (p != null))
									{
										scripter.DefineEventHandler(e, p);
										this[n].op = OP_CALL_ADD_EVENT;
									}
								}
							}
							else
							{
								if (f.GetParamTypeId(0) == (int) StandardType.Object)
								{
									string s = f.FullName;
									if (s == "System.Console.Write" || s == "System.Console.WriteLine")
									{
										int i = n;
										for (;;)
										{
											i--;
											if (this[i].op == OP_PUSH && this[i].res == f.Id)
												break;
											if (i == 0)
											{
												// Internal compiler error
												scripter.CreateErrorObject(Errors.CS0001);
												return;
											}
										}

										r = this[i];

										int type_id = GetTypeId(r.arg1);
										ClassObject c = GetClassObject(type_id);
										FunctionObject best;
										bool upcase = GetUpcase(n);

										sub_id = c.FindMethodId("ToString", null, null, 0, out best, upcase);

										if (sub_id > 0)
										{
											int res = AppVar(symbol_table[r.arg1].Level, (int) StandardType.String);

											InsertOperators(i, 2);

											this[i].op = OP_PUSH;
											this[i].arg1 = r.arg1;
											this[i].arg2 = 0;
											this[i].arg2 = 0;

											i++;
											this[i].op = OP_CALL;
											this[i].arg1 = sub_id;
											this[i].arg2 = 0;
											this[i].res = res;

											r.arg1 = res;

											n++;
											n++;
										}
									}
								}
							}
						}
					}
				}
			}

#if cf
			n = init_n;
			for (;;)
			{
				r = this[n];
				if (r.op == OP_PUSH && r.res > 0)
				{
					object x = symbol_table[r.res].Val;
					if (x is FunctionObject)
					{
						FunctionObject f = x as FunctionObject;
						int id1 = r.arg1;
						int param_number = r.arg2;
						int id2 = f.GetParamId(param_number);

						int t1 = symbol_table[id1].TypeId;
						int t2 = symbol_table[id2].TypeId;
						if (t1 != t2 && f.Imported)
						{
							int rop = 0;
							if (IsNumericTypeId(t1) && IsNumericTypeId(t2))
							{
								int t = t2;
								if (t == (int) StandardType.Sbyte)
									rop = OP_TO_SBYTE;
								else if (t == (int) StandardType.Byte)
									rop = OP_TO_BYTE;
								else if (t == (int) StandardType.Uint)
									rop = OP_TO_UINT;
								else if (t == (int) StandardType.Int)
									rop = OP_TO_INT;
								else if (t == (int) StandardType.Ushort)
									rop = OP_TO_USHORT;
								else if (t == (int) StandardType.Short)
									rop = OP_TO_SHORT;
								else if (t == (int) StandardType.Ulong)
									rop = OP_TO_ULONG;
								else if (t == (int) StandardType.Long)
									rop = OP_TO_LONG;
								else if (t == (int) StandardType.Char)
									rop = OP_TO_CHAR;
								else if (t == (int) StandardType.Float)
									rop = OP_TO_FLOAT;
								else if (t == (int) StandardType.Decimal)
									rop = OP_TO_DECIMAL;
								else if (t == (int) StandardType.Double)
									rop = OP_TO_DOUBLE;
							}

							if (rop != 0)
							{
								int temp_id = AppVar(symbol_table[id1].Level, t2);

								InsertOperators(n, 1);
								this[n].op = rop;
								this[n].arg1 = t2;
								this[n].arg2 = r.arg1;
								this[n].res = temp_id;

								r.arg1 = temp_id;
								n++;
							}
						}
					}
				}

				n++;
				if (n >= Card) break;
			}
#endif
		}

		/// <summary>
		/// Inserts event handlers (VB.NET only).
		/// </summary>
		public void InsertEventHandlers()
		{
			PaxLanguage language = PaxLanguage.CSharp;

			n = 0;
			for (;;)
			{
				n++;
				if (n >= card)
					break;

				if (this[n].op == OP_BEGIN_MODULE)
					language = (PaxLanguage) this[n].arg2;
				if (language == PaxLanguage.VB)
				{
					if (this[n].op == OP_ASSIGN)
					{
						int id = this[n].res;
						if (symbol_table[id].Kind == MemberKind.Field)
						{
							FieldObject fo = GetFieldObject(id);
							if (fo.HasModifier(Modifier.WithEvents))
							{
								for (int j = 1; j <= card; j++)
								{
									if (this[j].op == OP_ADD_HANDLES && this[j].arg2 == id)
									{
										int save_n = n;

										int temp_id = this[n].arg2;
										bool ok = false;
										for (int k = n; k >= 1; k--)
										{
											if (this[k].op == OP_CREATE_OBJECT && this[k].res == temp_id)
											{
												ok = true;
												n = k;
												id = temp_id;
												break;
											}
										}

										if (!ok)
											continue;


										int method_id = this[j].arg1;
										int type_id = symbol_table[id].TypeId;

										ClassObject c = GetClassObject(type_id);
										int event_name_index = symbol_table[this[j].res].NameIndex;

										MemberObject m = c.GetMemberByNameIndex(event_name_index, GetUpcase(n));
										if (m == null)
										{
											string event_name = symbol_table[this[j].res].Name;
											n = j;
											scripter.CreateErrorObjectEx(Errors.UNDECLARED_IDENTIFIER, event_name);
											return;
										}

										if (m.Kind != MemberKind.Event)
										{
										   // "CS0118. '{0}' denotes a '{1}' where a '{2}' was expected.";
											string event_name = symbol_table[this[j].res].Name;
											n = j;
											scripter.CreateErrorObjectEx(Errors.CS0118, event_name, m.Kind.ToString(), "event");
											return;
										}

										EventObject e = m as EventObject;
										int add_method_id = e.AddId;
										int delegate_type_id = symbol_table[e.Id].TypeId;

										int object_id = AppVar(symbol_table[id].Level, delegate_type_id);

										int target_id = 0;
										FunctionObject f = GetFunctionObject(method_id);
										if (f.Static)
											target_id = f.Owner.Id;
										else
											target_id = id;

										InsertOperators(n + 1, 8);

										n++;
										this[n].op = OP_CREATE_OBJECT;
										this[n].arg1 = delegate_type_id;
										this[n].arg2 = 0;
										this[n].res = object_id;

										n++;
										this[n].op = OP_PUSH;
										this[n].arg1 = target_id;
										this[n].arg2 = 0;
										this[n].res = delegate_type_id;

										n++;
										this[n].op = OP_PUSH;
										this[n].arg1 = method_id;
										this[n].arg2 = 0;
										this[n].res = delegate_type_id;

										n++;
										this[n].op = OP_PUSH;
										this[n].arg1 = object_id;
										this[n].arg2 = 0;
										this[n].res = 0;

										n++;
										this[n].op = OP_SETUP_DELEGATE;
										this[n].arg1 = 0;
										this[n].arg2 = 0;
										this[n].res = 0;

										n++;
										this[n].op = OP_PUSH;
										this[n].arg1 = object_id;
										this[n].arg2 = 0;
										this[n].res = add_method_id;

										n++;
										this[n].op = OP_PUSH;
										this[n].arg1 = id;
										this[n].arg2 = 0;
										this[n].res = 0;

										n++;
										this[n].op = OP_CALL;
										this[n].arg1 = add_method_id;
										this[n].arg2 = 1;
										this[n].res = 0;

										n = save_n + 8;
									}
								}
							}

/*
  359 Module:1 Line:79 		m.MH += new MenuHandler(t.Proc);
  360        CREATE OBJECT                      MenuHandler[  119]                        [    0]                    $$75[  273]
  361                 PUSH                                t[  267]                        [    0]             MenuHandler[  119]
  362                 PUSH                             Proc[  239]                        [    0]             MenuHandler[  119]
  363                 PUSH                             $$75[  273]                        [    0]                        [    0]
  364       SETUP DELEGATE                                 [    0]                        [    0]                        [    0]
  365                 PUSH                             $$75[  273]                        [    0]                  add_MH[  149]
  366                 PUSH                                m[  255]                        [    0]                        [    0]
  367                 CALL                           add_MH[  149]                    Void[    1]                                 init=86
*/

						}
					}
				}
			}

			n = 0;
			for (;;)
			{
				n++;
				if (n >= card)
					break;

				if (this[n].op == OP_BEGIN_MODULE)
					language = (PaxLanguage) this[n].arg2;
				if (this[n].op == OP_ADD_HANDLES)
				{
					int method_id = this[n].arg1;
					FunctionObject f = GetFunctionObject(method_id);
					if (f.Static)
						continue;

					int event_name_index = symbol_table[this[n].res].NameIndex;

					int type_id = symbol_table[this[n].arg2].TypeId;
					ClassObject c = GetClassObject(type_id);

					MemberObject m = c.GetMemberByNameIndex(event_name_index, GetUpcase(n));
					if (m == null)
					{
						string event_name = symbol_table[this[n].res].Name;
						scripter.CreateErrorObjectEx(Errors.UNDECLARED_IDENTIFIER, event_name);
						return;
					}

					if (m.Kind != MemberKind.Event)
					{
					   // "CS0118. '{0}' denotes a '{1}' where a '{2}' was expected.";
						string event_name = symbol_table[this[n].res].Name;
						scripter.CreateErrorObjectEx(Errors.CS0118, event_name, m.Kind.ToString(), "event");
						return;
					}

					EventObject e = m as EventObject;
					int add_method_id = e.AddId;
					int delegate_type_id = symbol_table[e.Id].TypeId;

					int constructor_id = (f.Owner as ClassObject).FindConstructorId();

					if (constructor_id == 0)
					{
						// internal error
						scripter.CreateErrorObject(Errors.CS0001);
						return;
					}

					int insert_point = 0;
					for (int j = 0; j <= card; j++)
						if (this[j].op == OP_END_METHOD && this[j].arg1 == constructor_id)
						{
							insert_point = j;
							break;
						}

					int this_id = symbol_table.GetThisId(constructor_id);
					int id = AppVar(this_id, symbol_table[this[n].arg2].TypeId);
					symbol_table[id].Name = symbol_table[this[n].arg2].Name;
					symbol_table[id].Kind = MemberKind.Ref;

					int object_id = AppVar(constructor_id, delegate_type_id);
					int target_id = this_id;

					int save_n = n;

					n = insert_point;

					InsertOperators(n, 9);

					this[n].op = OP_CREATE_REFERENCE;
					this[n].arg1 = this_id;
					this[n].arg2 = 0;
					this[n].res = id;

					n++;
					this[n].op = OP_CREATE_OBJECT;
					this[n].arg1 = delegate_type_id;
					this[n].arg2 = 0;
					this[n].res = object_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = target_id;
					this[n].arg2 = 0;
					this[n].res = delegate_type_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = method_id;
					this[n].arg2 = 0;
					this[n].res = delegate_type_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = object_id;
					this[n].arg2 = 0;
					this[n].res = 0;

					n++;
					this[n].op = OP_SETUP_DELEGATE;
					this[n].arg1 = 0;
					this[n].arg2 = 0;
					this[n].res = 0;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = object_id;
					this[n].arg2 = 0;
					this[n].res = add_method_id;

					n++;
					this[n].op = OP_PUSH;
					this[n].arg1 = id;
					this[n].arg2 = 0;
					this[n].res = 0;

					n++;
					this[n].op = OP_CALL;
					this[n].arg1 = add_method_id;
					this[n].arg2 = 1;
					this[n].res = 0;

					n = save_n + 9;

				}
			}
		}

		/// <summary>
		/// Performs call static constructors for all script-defined clases.
		/// </summary>
		public void CallStaticConstructors()
		{
			for (int id = 1; id <= symbol_table.Card; id++)
			{
				if (symbol_table[id].Kind == MemberKind.Constructor)
				{
					FunctionObject f = GetFunctionObject(id);
					if ((f.Static) && (!(f.Imported)))
						CallMethod(RunMode.Run, null, f.Id, null);
				}
			}
		}

		/// <summary>
		/// Creates new namespace.
		/// </summary>
		void OperCreateNamespace()
		{
			int class_id = r.arg1;
			int owner_id = r.arg2;

			string full_name = symbol_table[class_id].FullName;

			bool upcase = GetUpcase();

			if (scripter.FindAvailableType(full_name, upcase) != null)
			{
				// Cannot declare a namespace and a type both named '0'
				scripter.CreateErrorObjectEx(Errors.CS0010, full_name);
				return;
			}

			ClassObject o = GetClassObject(owner_id);
			if (o != null)
			{
				int idx = symbol_table[class_id].NameIndex;

				MemberObject m = o.GetMemberByNameIndex(idx, upcase);
				if (m != null)
				{
					if (m.Kind == MemberKind.Type)
					{
						ClassObject mc = m as ClassObject;
						if (mc.Class_Kind == ClassKind.Namespace)
						{
                            if (class_id == m.Id)
                                return;

							symbol_table[class_id].Kind = MemberKind.None;
							ReplaceId(class_id, m.Id);
							return;
						}
					}
					string name = symbol_table[class_id].Name;
					// The namespace 'namespace' already contains a definition for 'type'
					scripter.CreateErrorObjectEx(Errors.CS0101, o.Name, name);
					return;
				}
			}

			ClassKind ck = (ClassKind) r.res;
			ClassObject c = new ClassObject(scripter, class_id, owner_id, ck);
			c.PCodeLine = n;

			PutVal(class_id, c);

			if (o != null)
				o.AddMember(c);
			n++;
		}

		/// <summary>
		/// Creates using alias.
		/// </summary>
		void OperCreateUsingAlias()
		{
			int idx = symbol_table[r.arg1].NameIndex;

			bool upcase = GetUpcase();

			ClassObject c = GetClassObject(r.arg2);
			MemberObject m = c.GetMemberByNameIndex(idx, upcase);
			if (m == null)
			{
				m = new MemberObject(scripter, r.arg1, c.Id);
				m.PCodeLine = n;

				m.Kind = MemberKind.Alias;
				c.AddMember(m);

				PutVal(m.Id, m);
			}
			else
			{
				// The using alias 'alias' appeared previously in this namespace
				scripter.CreateErrorObjectEx(Errors.CS1537, m.Name);
				return;
			}

			symbol_table[r.arg1].Kind = MemberKind.Alias;
			n++;
		}

		/// <summary>
		/// Creates new script-defined type.
		/// </summary>
		void OperCreateClass()
		{
			int class_id = r.arg1;
			int owner_id = r.arg2;
			ClassObject o = GetClassObject(owner_id);

			if (scripter.HasPredefinedNamespace(symbol_table[class_id].FullName))
			{
				scripter.CreateErrorObjectEx(Errors.CS0519, symbol_table[class_id].FullName);
				return;
			}

			bool upcase = GetUpcase();

			int idx = symbol_table[class_id].NameIndex;
			MemberObject m = o.GetMemberByNameIndex(idx, upcase);
			if (m != null)
			{
				string name = symbol_table[class_id].Name;
				if (o.Class_Kind == ClassKind.Namespace)
					// The namespace 'namespace' already contains a definition for 'type'
					scripter.CreateErrorObjectEx(Errors.CS0101, o.Name, name);
				else
					// The class 'class' already contains a definition for 'identifier'
					scripter.CreateErrorObjectEx(Errors.CS0102, o.Name, name);
				return;
			}

			ClassKind ck = (ClassKind) r.res;
			ClassObject c = new ClassObject(scripter, class_id, owner_id, ck);

			c.PCodeLine = n;

			PutVal(class_id, c);
			o.AddMember(c);
			n++;
		}

		/// <summary>
		/// Creates new field of type.
		/// </summary>
		void OperCreateField()
		{
			int field_id = r.arg1;
			int owner_id = r.arg2;
			ClassObject c = GetClassObject(owner_id);

			bool upcase = GetUpcase();

			int idx = symbol_table[field_id].NameIndex;
			MemberObject m = c.GetMemberByNameIndex(idx, upcase);
			if (m != null)
			{
				string name = symbol_table[field_id].Name;
				// The class 'class' already contains a definition for 'identifier'
				scripter.CreateErrorObjectEx(Errors.CS0102, c.Name, name);
				return;
			}

			FieldObject f = new FieldObject(scripter, field_id, owner_id);
			f.PCodeLine = n;
			PutVal(field_id, f);
			c.AddMember(f);

			n++;
		}

		/// <summary>
		/// Creates new property of type.
		/// </summary>
		void OperCreateProperty()
		{
			int property_id = r.arg1;
			int owner_id = r.arg2;
			int param_count = r.res;
			ClassObject c = GetClassObject(owner_id);

			PropertyObject p = new PropertyObject(scripter, property_id, owner_id, param_count);
			p.PCodeLine = n;
			PutVal(property_id, p);
			c.AddMember(p);

			n++;
		}

		/// <summary>
		/// Creates new read accessor of property.
		/// </summary>
		void OperAddReadAccessor()
		{
			int property_id = r.arg1;
			int sub_id = r.arg2;

			PropertyObject p = GetPropertyObject(property_id);
			p.ReadId = sub_id;
			n++;
		}

		/// <summary>
		/// Creates new write accessor of property.
		/// </summary>
		void OperAddWriteAccessor()
		{
			int property_id = r.arg1;
			int sub_id = r.arg2;

			PropertyObject p = GetPropertyObject(property_id);
			p.WriteId = sub_id;
			n++;
		}

		/// <summary>
		/// Sets property to be default.
		/// </summary>
		void OperSetDefault()
		{
			int property_id = r.arg1;
			int sub_id = r.arg2;

			PropertyObject p = GetPropertyObject(property_id);
			p.IsDefault = true;
			n++;
		}

		/// <summary>
		/// Creates new event of type.
		/// </summary>
		void OperCreateEvent()
		{
			int event_id = r.arg1;
			int owner_id = r.arg2;
			int param_count = r.res;
			ClassObject c = GetClassObject(owner_id);

			bool upcase = GetUpcase();

			int idx = symbol_table[event_id].NameIndex;
			MemberObject m = c.GetMemberByNameIndex(idx, upcase);
			if (m != null)
			{
				string name = symbol_table[event_id].Name;
				// The class 'class' already contains a definition for 'identifier'
				scripter.CreateErrorObjectEx(Errors.CS0102, c.Name, name);
				return;
			}

			EventObject e = new EventObject(scripter, event_id, owner_id);
			e.PCodeLine = n;
			PutVal(event_id, e);
			c.AddMember(e);

			n++;
		}

		/// <summary>
		/// Creates add accessor of event.
		/// </summary>
		void OperAddAddAccessor()
		{
			int event_id = r.arg1;
			int sub_id = r.arg2;

			EventObject e = GetEventObject(event_id);
			e.AddId = sub_id;
			n++;
		}

		/// <summary>
		/// Creates remove accessor of event.
		/// </summary>
		void OperAddRemoveAccessor()
		{
			int event_id = r.arg1;
			int sub_id = r.arg2;

			EventObject e = GetEventObject(event_id);
			e.RemoveId = sub_id;
			n++;
		}

		/// <summary>
		/// Adds modifier to a member.
		/// </summary>
		void OperAddModifier()
		{
			int member_id = r.arg1;
			Modifier modifier = (Modifier) r.arg2;
			MemberObject mo = GetMemberObject(member_id);
			mo.AddModifier(modifier);
			int owner_id = symbol_table[member_id].Level;
			ClassObject c = GetClassObject(owner_id);
			n++;
		}

		/// <summary>
		/// Creates new method of type.
		/// </summary>
		void OperCreateMethod()
		{
			int sub_id = r.arg1;
			int owner_id = r.arg2;
			FunctionObject f = new FunctionObject(scripter, sub_id, owner_id);
			PutVal(sub_id, f);
			ClassObject c = GetClassObject(owner_id);
			c.AddMember(f);
			n++;
		}

		/// <summary>
		/// Creates "pattern method" of a delegate type.
		/// </summary>
		void OperAddPattern()
		{
			ClassObject c = GetClassObject(r.arg1);
			c.PatternMethod = GetFunctionObject(r.arg2);
			n++;
		}

		/// <summary>
		/// Adds explicit interface method.
		/// </summary>
		void OperAddExplicitInterface()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			ClassObject c = GetClassObject(r.arg2);
			f.ExplicitInterface = c;
			n++;
		}

		/// <summary>
		/// Adds parameter to a method.
		/// </summary>
		void OperAddParam()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			f.AddParam(r.arg2, (ParamMod) r.res);
			n++;
		}

		/// <summary>
		/// Assign default value to parameter
		/// </summary>
		void OperAddDefaultValue()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			f.AddDefaultValueId(r.arg2, r.res);
			n++;
		}

		/// <summary>
		/// Assign min value to class definition.
		/// </summary>
		void OperAddMinValue()
		{
			ClassObject c = GetClassObject(r.arg1);
			c.MinValueId = r.arg2;
			n++;
		}

		/// <summary>
		/// Assign max value to class definition.
		/// </summary>
		void OperAddMaxValue()
		{
			ClassObject c = GetClassObject(r.arg1);
			c.MaxValueId = r.arg2;
			n++;
		}

		/// <summary>
		/// Adds event field to a class.
		/// </summary>
		void OperAddEventField()
		{
			EventObject e = GetEventObject(r.arg1);
			e.EventFieldId = r.arg2;
			n++;
		}

		/// <summary>
		/// Adds 'params' parameter to a method.
		/// </summary>
		void OperAddParams()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			f.AddParam(r.arg2, (ParamMod) r.res);
			f.ParamsId = r.arg2;

			int array_type_id = symbol_table[f.ParamsId].TypeId;
			string element_type_name = PaxSystem.GetElementTypeName(symbol_table[array_type_id].Name);
			int element_type_id = scripter.GetTypeId(element_type_name);

			f.ParamsElementId = AppVar(0, element_type_id);

			n++;
		}

		/// <summary>
		/// Adds entry point to a method.
		/// </summary>
		void OperInitMethod()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			n++; // op_go
			f.Init = this[n + 1];
		}

		/// <summary>
		/// Checks declared local variables of a method.
		/// </summary>
		void OperDeclareLocalVariable()
		{
			int id = r.arg1;
			int sub_id = r.arg2;

			r.op = OP_DECLARE_LOCAL_VARIABLE_RUNTIME;

			bool assigned = false;
			bool used = false;

			if (symbol_table[id].Kind == MemberKind.Label)
				used = true;

			int type_id = GetTypeId(id);
			ClassObject c = GetClassObject(type_id);
			if (c.IsStruct)
				assigned = true;

			int i = n;

			for (;;)
			{
				i++;

				int op = this[i].op;

				if (op == OP_SEPARATOR)
					continue;
				if (op == OP_END_METHOD)
					break;

				bool ok = false;
				ok |= op == OP_ASSIGN;
				ok |= op == OP_ASSIGN_STRUCT;
				ok |= op == OP_PUSH;
				ok |= op == OP_CALL;
				ok |= op == OP_CALL_VIRT;
				ok |= op == OP_CREATE_OBJECT;
				ok |= op == OP_CALL_SIMPLE;
				ok |= op == OP_CALL_BASE;
				ok |= op == OP_CREATE_REFERENCE;
				ok |= op == OP_CAST;
				ok |= op == OP_GO_FALSE;
				ok |= op == OP_GO_TRUE;
				ok |= overloadable_binary_operators_str[op] != null;
				ok |= overloadable_unary_operators_str[op] != null;

				if (!ok)
					continue;

				if ((op == OP_ASSIGN) && (this[i].arg1 == id))
				{
					used = true;
					assigned = true;
				}
				else if ((op == OP_ASSIGN_STRUCT) && (this[i].arg1 == id))
				{
					used = true;
					assigned = true;
				}
				else if ((op == OP_CALL) && (this[i].res == id))
				{
					assigned = true;
				}
				else if ((op == OP_CALL_VIRT) && (this[i].res == id))
				{
					assigned = true;
				}
				else if ((op == OP_CALL_SIMPLE) && (this[i].res == id))
				{
					assigned = true;
				}
				else if ((op == OP_CALL_BASE) && (this[i].res == id))
				{
					assigned = true;
				}
				else if ((op == OP_CREATE_OBJECT) && (this[i].res == id))
				{
					assigned = true;
				}
				else if (op == OP_PUSH && this[i].arg1 == id)
				{
					used = true;

					if (this[i].arg2 == (int) ParamMod.Out)
						assigned = true;

					if (!assigned)
					{
						int n1 = n;
						n = i;
						// Use of unassigned local variable 'var'
						scripter.CreateErrorObjectEx(Errors.CS0165,
							symbol_table[id].Name);
						n = n1;
						break;
					}
				}
				else if (op == OP_CREATE_REFERENCE && this[i].arg1 == id)
				{
					used = true;
				}
				else if ((this[i].arg2 == id) || (this[i].res == id))
				{
					used = true;
					if (!assigned)
					{
						int n1 = n;
						n = i;
						// Use of unassigned local variable 'var'
						scripter.CreateErrorObjectEx(Errors.CS0165,
							symbol_table[id].Name);
						n = n1;
						break;
					}
				}
			}

			if (!used)
			{
				i = n;
				for (;;)
				{
					i++;
					if (this[i].arg1 == id)
						used = true;
					if (this[i].arg2 == id)
						used = true;
					if (this[i].res == id)
						used = true;
					if (this[i].op == OP_END_METHOD)
					break;
				}
				if (assigned)
				{
					if (!used)
						// The variable 'variable' is assigned but its value is never used
						scripter.CreateWarningObjectEx(Errors.CS0219, symbol_table[id].Name);
				}
				else
				{
					if (!used)
						// The variable 'var' is declared but never used
						scripter.CreateWarningObjectEx(Errors.CS0168, symbol_table[id].Name);
				}
			}

			n++;
		}

		/// <summary>
		/// Ends creation of method.
		/// </summary>
		void OperEndMethod()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			f.SetupLowBound(r.arg2);
			f.SetupParameters();
			n++;
		}

// RUN-TIME OPERATORS

		/// <summary> OP_NOP </summary>
		void OperNop()
		{
			n++;
		}

		/// <summary> OP_GO </summary>
		void OperGo()
		{
			n = r.arg1;
		}

		/// <summary> OP_GO_TRUE </summary>
		void OperGoTrue()
		{
			if (GetValueAsBool(r.arg2))
				n = r.arg1;
			else
				n++;
		}

		/// <summary> OP_GO_FALSE </summary>
		void OperGoFalse()
		{
			if (GetValueAsBool(r.arg2))
				n++;
			else
				n = r.arg1;
		}

		/// <summary> OP_GO_NULL </summary>
		void OperGoNull()
		{
			if (GetValue(r.arg2) == null)
				n = r.arg1;
			else
				n++;
		}

		/// <summary> OP_GO_START </summary>
		void OperGotoStart()
		{
			goto_line = r.arg1;
			OperGotoContinue();
		}

		/// <summary> OP_GO_CONTINUE </summary>
		void OperGotoContinue()
		{
			if (goto_line == 0)
			{
				n++;
				return;
			}
			for (;;)
			{
				r = (ProgRec) prog[n];
				int op = r.op;

				if (n == goto_line)
				{
					goto_line = 0;
					break;
				}
				if (op == OP_FINALLY)
				{
					if (try_stack.Legal(n))
						break;
				}
				else if ((op == OP_RET) || (op == OP_HALT))
				{
					if (goto_line > 0)
					{
						n = goto_line;
						goto_line = 0;
					}
					break;
				}
				else
					n++;
			}
		}

		void OperSwappedArguments()
		{
			scripter.SwappedArguments = (bool) scripter.GetValue(r.arg1);
			n++;
		}

		/// <summary> Save checked state </summary>
		void SaveCheckedState()
		{
			checked_stack.Push(Checked);
		}

		/// <summary> Restore checked state operator. </summary>
		void RestoreCheckedState()
		{
			Checked = (bool) checked_stack.Pop();
		}

		/// <summary> OP_CHECKED </summary>
		void OperChecked()
		{
			Checked = (bool) GetVal(r.arg1);
			SaveCheckedState();
			n++;
		}

		/// <summary> OP_RESTORE_CHECKED_STATE </summary>
		void OperRestoreCheckedState()
		{
			RestoreCheckedState();
			n++;
		}

		/// <summary> OP_LOCK_ON </summary>
		void OperLock()
		{
			ObjectObject v = GetObjectObject(r.arg1);
			System.Threading.Monitor.Enter(v.Instance);
			n++;
		}

		/// <summary> OP_LOCK_OFF </summary>
		void OperUnlock()
		{
			ObjectObject v = GetObjectObject(r.arg1);
			System.Threading.Monitor.Exit(v.Instance);
			n++;
		}

		/// <summary> OP_DISPOSE </summary>
		void OperDispose()
		{
			ObjectObject v = GetObjectObject(r.arg1);
			if (v.Class_Object.Imported && v.Instance != null)
				((IDisposable)v.Instance).Dispose();
			else
			{
				string s = v.Class_Object.FullName + ".Dispose";
				int sub_id = symbol_table.LookupFullName(s, false);
				if (sub_id != 0)
				{
					CallMethodEx(RunMode.Run, v, sub_id);
					Terminated = false;
				}
			}
			n++;
		}

		/// <summary> OP_TYPEOF </summary>
		void OperTypeOf()
		{
			ClassObject c = GetClassObject(r.arg1);
			PutValue(r.res, scripter.ToScriptObject(c.RType));
            n++;
		}

		/// <summary> OP_PUSH </summary>
		void OperPush()
		{
			stack.Push(GetValue(r.arg1));
			n++;
		}

		/// <summary> OP_ADD_DELEGATES. Saves delegates into private fied of event. </summary>
		void OperAddDelegates()
		{
			object v1 = GetValue(r.arg1);
			if (v1 == null)
				PutVal(r.res, GetObjectObject(r.arg2));
			else
			{
				ObjectObject d1 = GetObjectObject(r.arg1);
				ObjectObject d2 = GetObjectObject(r.arg2);

				ClassObject c = d1.Class_Object;
				ObjectObject result = c.CreateObject();

				object x;
				FunctionObject f;
				bool b;

				b = d1.FindFirstInvocation(out x, out f);
				while (b)
				{
					result.AddInvocation(x, f);
					b = d1.FindNextInvocation(out x, out f);
				}

				b = d2.FindFirstInvocation(out x, out f);
				while (b)
				{
					result.AddInvocation(x, f);
					b = d2.FindNextInvocation(out x, out f);
				}

				PutVal(r.res, result);
			}
			n++;
		}

		/// <summary> OP_SUB_DELEGATES </summary>
		void OperSubDelegates()
		{
			ObjectObject d1 = GetObjectObject(r.arg1);
			ObjectObject d2 = GetObjectObject(r.arg2);
			ClassObject c = d1.Class_Object;
			ObjectObject result = c.CreateObject();

			object x;
			FunctionObject f;
			bool b;

			b = d1.FindFirstInvocation(out x, out f);
			while (b)
			{
				result.AddInvocation(x, f);
				b = d1.FindNextInvocation(out x, out f);
			}

			b = d2.FindFirstInvocation(out x, out f);
			while (b)
			{
				result.SubInvocation(x, f);
				b = d2.FindNextInvocation(out x, out f);
			}

			PutVal(r.res, result);
			n++;
		}

		/// <summary>
		/// Compare Delegates.
		/// </summary>
		bool CompareDelegates(ObjectObject d1, ObjectObject d2)
		{
			if (d1.InvocationCount == d2.InvocationCount)
			{
				object x1, x2;
				FunctionObject f1, f2;
				bool b;

				b = d1.FindFirstInvocation(out x1, out f1);
				b = d2.FindFirstInvocation(out x2, out f2);
				while (b)
				{
					if ((x1 != x2) || (f1 != f2))
						return false;
					b = d1.FindNextInvocation(out x1, out f1);
					b = d2.FindNextInvocation(out x2, out f2);
				}
				return true;
			}
			else
				return false;
		}

		/// <summary> OP_EQ_DELEGATES </summary>
		void OperEqDelegates()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			if ((v1 == null) || (v2 == null))
				PutVal(r.res, v1 == v2);
			else
			{
				ObjectObject d1 = GetObjectObject(r.arg1);
				ObjectObject d2 = GetObjectObject(r.arg2);
				PutVal(r.res, CompareDelegates(d1, d2));
			}
			n++;
		}

		/// <summary> OP_NE_DELEGATES </summary>
		void OperNeDelegates()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			if ((v1 == null) || (v2 == null))
				PutVal(r.res, v1 != v2);
			else
			{
				ObjectObject d1 = GetObjectObject(r.arg1);
				ObjectObject d2 = GetObjectObject(r.arg2);
				PutVal(r.res, !CompareDelegates(d1, d2));
			}
			n++;
		}

		/// <summary> OP_SETUP_DELEGATE </summary>
		void OperSetupDelegate()
		{
			ObjectObject d = PopObjectObject(); // delegate object
			FunctionObject f = PopFunctionObject(); // method
			object x = stack.Pop(); // instance: ObjectObject or ClassObject

			d.AddInvocation(x, f);

			n++;
		}

		/// <summary> OP_FIND_FIRST_DELEGATE </summary>
		void OperFindFirstDelegate()
		{
			ObjectObject d = GetObjectObject(r.arg1);
			object x;
			FunctionObject f;
			d.FindFirstInvocation(out x, out f);
			PutVal(r.arg2, f); // code
			PutVal(r.res, x); // data
			n++;
		}

		/// <summary> OP_FIND_NEXT_DELEGATE </summary>
		void OperFindNextDelegate()
		{
			ObjectObject d = GetObjectObject(r.arg1);
			object x;
			FunctionObject f;
			d.FindNextInvocation(out x, out f);
			PutVal(r.arg2, f); // code
			PutVal(r.res, x); // data
			n++;
		}

		/// <summary> OP_CALL_BASE </summary>
		void OperCallBase()
		{
			OperCall();
		}

		/// <summary> OP_CALL </summary>
		void OperCall()
		{
			FunctionObject f = GetFunctionObject(r.arg1);

			if (f.Imported)
			{
				OperHostCall();
				return;
			}
			object v = stack.Pop();

			f.AllocateSub();
			f.PutThis(v);

			CallStackRec csr = null;
			if (debugging)
				csr = new CallStackRec(scripter, f, n);

			int param_count = r.arg2;
			for (int i = 0; i < param_count; i++)
			{
				v = stack.Pop();
				f.PutParam(param_count - 1 - i, v);

				if (debugging)
					csr.Parameters.Insert(0, v);
			}

			if (debugging)
				callstack.Add(csr);

			stack.Push(n);
			n = f.Init.FinalNumber;

			curr_stack_count = stack.Count;
		}

		/// <summary> OP_CALL_VIRT </summary>
		void OperCallVirt()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			object v = stack.Pop();

			int type_id = symbol_table[this[n - 1].arg1].TypeId;
			ClassObject c = GetClassObject(type_id);

			if (c.IsInterface)
			{
				ObjectObject o = scripter.ToScriptObject(v);
				type_id = o.Class_Object.Id;
			}

			f = f.GetLateBindingFunction(v, type_id, GetUpcase());

			if (f.Imported)
			{
				stack.Push(v);
				OperHostCall();
				return;
			}

			f.AllocateSub();
			f.PutThis(v);

			CallStackRec csr = null;
			if (debugging)
				csr = new CallStackRec(scripter, f, n);

			int param_count = r.arg2;
			for (int i=0; i<param_count; i++)
			{
				v = stack.Pop();
				f.PutParam(param_count - 1 - i, v);

				if (debugging)
					csr.Parameters.Insert(0, v);
			}

			if (debugging)
				callstack.Add(csr);

			stack.Push(n);
			n = f.Init.FinalNumber;
		}

		/// <summary> OP_GET_PARAM_VALUE </summary>
		void OperGetParamValue()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			object v = f.GetParamValue(r.arg2);
			PutVal(r.res, v);
			n++;
		}

        /// <summary> OP_REDIM </summary>
        void OperRedim()
        {
            ObjectObject so = GetObjectObject(r.arg1);

            ClassObject c = so.Class_Object;

            int param_count = r.arg2;
            int[] parameters = new int[param_count];

            for (int i = 0; i < param_count; i++)
            {
                object p = stack.Pop();

                if (p == null)
                    parameters[param_count - 1 - i] = 0;
                else if (p.GetType() == typeof(ObjectObject))
                {
                    ObjectObject o = p as ObjectObject;
                    parameters[param_count - 1 - i] = Conversion.ToInt(o.Instance) + 1;
                }
                else
                    parameters[param_count - 1 - i] = Conversion.ToInt(p) + 1;
            }

            Type t;
            if (c.ImportedType != null)
                t = c.ImportedType;
            else
                t = typeof(object);

            if (t == typeof(Int32[]))
            {
                Int32[] a = so.Instance as Int32[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Int64[]))
            {
                Int64[] a = so.Instance as Int64[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Int16[]))
            {
                Int16[] a = so.Instance as Int16[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Boolean[]))
            {
                Boolean[] a = so.Instance as Boolean[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Byte[]))
            {
                Byte[] a = so.Instance as Byte[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(SByte[]))
            {
                SByte[] a = so.Instance as SByte[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Char[]))
            {
                Char[] a = so.Instance as Char[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Decimal[]))
            {
                Decimal[] a = so.Instance as Decimal[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Double[]))
            {
                Double[] a = so.Instance as Double[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(Single[]))
            {
                Single[] a = so.Instance as Single[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else if (t == typeof(String[]))
            {
                String[] a = so.Instance as String[];
                Array.Resize(ref a, parameters[0]);
                so.Instance = a;
            }
            else
            {
                System.Array array_instance = (System.Array) so.Instance;            
                int l = array_instance.Length;

                object[] buff = new object[l];

                for (int i = 0; i < l - 1; i++) 
                {
                    buff[i] = array_instance.GetValue(i);
                }

                so.Instance = Array.CreateInstance(t, parameters);
                array_instance = (System.Array) so.Instance;
                if (l > array_instance.Length)
                    l = array_instance.Length;
                for (int i = 0; i < l - 1; i++)
                {
                    array_instance.SetValue(buff[i], i);
                }
            }

            n++;
        }

		/// <summary> Calls host-defined method </summary>
		void OperHostCall()
		{
			bool temp = Terminated;
			ScripterState temp2 = scripter.Owner.State;

			scripter.Owner.SetInternalState(ScripterState.Running);

			FunctionObject f = GetFunctionObject(r.arg1);
			object v = stack.Pop();

			CallStackRec csr = null;
			if (debugging)
				csr = new CallStackRec(scripter, f, n);

			int param_count = f.ParamCount;
			for (int i = 0; i < param_count; i++)
			{
				object p = stack.Pop();

				if (debugging)
					csr.Parameters.Insert(0, p);

				if (p == null)
					f.Params[param_count - 1 - i] = p;
				else if (p.GetType() == typeof(ObjectObject))
				{
					ObjectObject o = p as ObjectObject;

					int param_id = f.Param_Ids[i];
					int param_type_id = symbol_table[param_id].TypeId;
					if (param_type_id == (int) StandardType.Object)
					{
						if (f.Owner.NamespaceNameIndex == symbol_table.SYSTEM_COLLECTIONS_ID ||
							f.Owner.Name == "ArrayList")
						{
							f.Params[param_count - 1 - i] = o;
							continue;
						}
					}

					f.Params[param_count - 1 - i] = o.Instance;
				}
				else
					f.Params[param_count - 1 - i] = p;
			}

			if (debugging)
				callstack.Add(csr);

			if (f.Kind == MemberKind.Constructor)
			{
				if ((f.Owner as ClassObject).HasModifier(Modifier.Abstract))
					(scripter.ToScriptObject(v)).Instance = new Object();
				else
					(scripter.ToScriptObject(v)).Instance = f.InvokeConstructor();
			}
			else
			{
				object result;
				if (f.Static)
				{
					if (v == null)
						result = f.InvokeMethod(null);
					else
					{
						Type t = (v as ClassObject).ImportedType;
						result = f.InvokeMethod(t);
					}
				}
				else
				{
					if (v is ObjectObject)
						result = f.InvokeMethod((v as ObjectObject).Instance);
					else
						result = f.InvokeMethod(v);
//					result = f.InvokeMethod((scripter.ToScriptObject(v)).Instance);
				}
				if (r.res != 0)
					PutValue(r.res, result);
			}

			if (debugging)
				callstack.Pop();

			Terminated = temp;
			scripter.Owner.SetInternalState(temp2);
			n++;
		}

		/// <summary> Calls host-defined delegate </summary>
		void OperDynamicInvoke()
		{
			bool temp = Terminated;

			Delegate f = GetValue(r.arg1) as Delegate;
			object v = stack.Pop();

			int param_count = r.arg2;
			object[] Params = new object[param_count];

			for (int i = 0; i < param_count; i++)
			{
				object p = stack.Pop();

				if (p == null)
					Params[param_count - 1 - i] = p;
				else if (p.GetType() == typeof(ObjectObject))
				{
					ObjectObject o = p as ObjectObject;
					Params[param_count - 1 - i] = o.Instance;
				}
				else
					Params[param_count - 1 - i] = p;
			}

#if cf
			object result = null;
#else
			object result = f.DynamicInvoke(Params);
#endif

			if (r.res != 0)
				PutValue(r.res, result);

			Terminated = temp;
			n++;
		}

		/// <summary> Creates instance of array </summary>
		void OperCreateArrayInstance()
		{
			ClassObject c = GetClassObject(r.arg1);
			object v = stack.Pop();

			int param_count = r.arg2;
			int[] parameters = new int[param_count];

			for (int i = 0; i < param_count; i++)
			{
				object p = stack.Pop();

				if (p == null)
					parameters[param_count - 1 - i] = 0;
				else if (p.GetType() == typeof(ObjectObject))
				{
					ObjectObject o = p as ObjectObject;
					parameters[param_count - 1 - i] = Conversion.ToInt(o.Instance);
				}
				else
					parameters[param_count - 1 - i] = Conversion.ToInt(p);
			}

			Type t;
			if (c.ImportedType != null)
				t = c.ImportedType;
			else
				t = typeof(object);

			scripter.ToScriptObject(v).Instance = Array.CreateInstance(t, parameters);

			n++;
		}

		/// <summary> OP_ADD_EVENT </summary>
		void OperCallAddEvent()
		{
			FunctionObject f = GetFunctionObject(r.arg1);
			if (!f.Imported)
			{
				OperCall();
				return;
			}

			object v = stack.Pop();
			if (v.GetType() == typeof(ObjectObject))
			{
				v = (v as ObjectObject).Instance;
			}

			object d = stack.Pop(); // script-defined delegate

			int param_type_id = symbol_table[f.Param_Ids[0]].TypeId;
			ClassObject paramClass = GetClassObject(param_type_id);
			if (paramClass.IsDelegate)
			{
				string event_name = f.Name.Substring(4);
				EventInfo e = v.GetType().GetEvent(event_name);
				FunctionObject p = paramClass.PatternMethod;
				if ((e != null) && (p != null))
				{
					Delegate del = scripter.CreateDelegate(v, e, p, d);
					e.AddEventHandler(v, del);
				}
			}
			n++;
		}

		/// <summary> OP_RET </summary>
		void OperRet()
		{
            if (r.arg1 == scripter.EntryId)
                CollectResults(r.arg1, scripter.Result_List);

			if (debugging)
				callstack.Pop();

			FunctionObject f = GetFunctionObject(r.arg1);

			n = (int) stack.Pop();
			r = (ProgRec) prog[n];

			object v = GetValue(f.ResultId);
			f.DeallocateSub();

			if (r.res != 0 )
				PutValue(r.res, v);

			n++;
		}

		/// <summary> OP_EXIT_SUB </summary>
		void OperExitSub()
		{
			do
			{
				n++;
				r = (ProgRec) prog[n];
				if (r.op == OP_RET)
					break;

			} while (true);
		}

		/// <summary> OP_CREATE_OBJECT </summary>
		void OperCreateObject()
		{
			int class_id = r.arg1;
			int object_id = r.res;
			ClassObject c = GetClassObject(class_id);
			ObjectObject o = c.CreateObject();
			PutValue(object_id, o);

			if (c.IsPascalArray)
			{
				Type t = c.ImportedType;

				ClassObject range_class = GetClassObject(c.RangeTypeId);

				int min_value = range_class.MinValue;
				int max_value = range_class.MaxValue;

				Array array_instance = Array.CreateInstance(t, max_value - min_value + 1);
				o.Instance = array_instance;

				ClassObject index_class = GetClassObject(c.IndexTypeId);

				if (!index_class.Imported)
				{

					for (int i = min_value; i <= max_value; i++)
					{
						object value = index_class.CreateObject();
						array_instance.SetValue(value, i - min_value);
					}

				}
			}

			n++;
		}

		/// <summary> OP_CREATE_REFERENCE </summary>
		void OperCreateReference()
		{
			PutVal(r.res, GetObjectObject(r.arg1));
			n++;
		}

		/// <summary> OP_CREATE_INDEX_OBJECT </summary>
		void OperCreateIndexObject()
		{
			ObjectObject o = GetObjectObject(r.arg1);

			IndexObject i = new IndexObject(scripter, o);
			PutVal(r.res, i);
			n++;

			ClassObject c = o.Class_Object;
			if (c.IsPascalArray)
			{
				ClassObject range_class = GetClassObject(c.RangeTypeId);
				i.MinValue = range_class.MinValue;
			}
		}

		/// <summary> OP_ADD_INDEX </summary>
		void OperAddIndex()
		{
			IndexObject i = GetIndexObject(r.arg1);
			object v = GetValue(r.arg2);
			i.AddIndex(v);
			n++;
		}

		/// <summary> OP_SETUP_INDEX_OBJECT </summary>
		void OperSetupIndexObject()
		{
			IndexObject i = GetIndexObject(r.arg1);
			i.Setup();
			n++;
		}

		/// <summary> OP_CAST </summary>
		void OperCast()
		{
			object v = GetValue(r.arg2);
			PutValue(r.res, v);
			n++;
		}

		/// <summary> OP_TO_SBYTE </summary>
		void OperToSbyte()
		{
			object v = Conversion.ToPrimitive(GetValue(r.arg2));
			int x = (int) Conversion.ChangeType(v, typeof(int));
			PutValue(r.res, (sbyte) x);
			n++;
		}

		/// <summary> OP_TO_BYTE </summary>
		void OperToByte()
		{
			PutValue(r.res, Conversion.ToByte(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_USHORT </summary>
		void OperToUshort()
		{
			object v = Conversion.ToPrimitive(GetValue(r.arg2));
			int x = (int) Conversion.ChangeType(v, typeof(int));
			PutValue(r.res, (ushort) x);
			n++;
		}

		/// <summary> OP_TO_SHORT </summary>
		void OperToShort()
		{
			PutValue(r.res, Conversion.ToShort(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_UINT </summary>
		void OperToUint()
		{
			object v = Conversion.ToPrimitive(GetValue(r.arg2));
			long x = (long) Conversion.ChangeType(v, typeof(long));
			PutValue(r.res, (uint) x);
			n++;
		}

		/// <summary> OP_TO_INT </summary>
		void OperToInt()
		{
			PutValue(r.res, Conversion.ToInt(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_ULONG </summary>
		void OperToUlong()
		{
			object v = Conversion.ToPrimitive(GetValue(r.arg2));
			PutValue(r.res, (ulong) Conversion.ChangeType(v, typeof(ulong)));
			n++;
		}

		/// <summary> OP_TO_LONG </summary>
		void OperToLong()
		{
			PutValue(r.res, Conversion.ToLong(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_CHAR </summary>
		void OperToChar()
		{
			PutValue(r.res, Conversion.ToChar(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_FLOAT </summary>
		void OperToFloat()
		{
			PutValue(r.res, Conversion.ToFloat(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_DOUBLE </summary>
		void OperToDouble()
		{
			PutValue(r.res, Conversion.ToDouble(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_DECIMAL </summary>
		void OperToDecimal()
		{
			PutValue(r.res, Conversion.ToDecimal(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_STRING </summary>
		void OperToString()
		{
			PutValue(r.res, Conversion.ToString(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_BOOLEAN </summary>
		void OperToBoolean()
		{
			PutValue(r.res, Conversion.ToBoolean(GetValue(r.arg2)));
			n++;
		}

		/// <summary> OP_TO_ENUM </summary>
		void OperToEnum()
		{
			ClassObject c = GetClassObject(r.arg1);
			PutValue(r.res, Conversion.ToEnum(c.RType, GetValue(r.arg2)));
			n++;
		}

		void OperToCharArray()
		{
			string s = Conversion.ToString(GetValue(r.arg2));
			char[] a = new char[s.Length];
			for (int i = 0; i < s.Length; i++)
				a[i] = s[i];
			PutValue(r.arg1, a);
			n++;
		}

		/// <summary> OP_TRY_ON </summary>
		void OperTryOn()
		{
			try_stack.Push(n, r.arg1);
			n++;
		}

		/// <summary> OP_TRY_OFF </summary>
		void OperTryOff()
		{
			try_stack.Pop();
			n++;
		}

		/// <summary> OP_THROW </summary>
		void OperThrow()
		{
			if (r.arg1 > 0)
			{
				int type_id = symbol_table[r.arg1].TypeId;
				ClassObject c = GetClassObject(type_id);
				ObjectObject v = scripter.ToScriptObject(GetValue(r.arg1));

				if (!c.Imported)
					custom_ex_list.AddObject(v.Instance as Exception, v);

				throw (v.Instance as Exception);
			}
			else
			{
				throw scripter.LastError.E;
			}
		}

		/// <summary> OP_CATCH </summary>
		void OperCatch()
		{
			n++;
		}

		/// <summary> OP_ONERROR </summary>
		void OperOnError()
		{
			n++;
		}

		/// <summary> RESUME </summary>
		void OperResume()
		{
			goto_line = resume_stack.Peek();
			resume_stack.Pop();
			OperGotoContinue();
		}

		/// <summary> RESUME NEXT </summary>
		void OperResumeNext()
		{
			goto_line = resume_stack.Peek() + 1;
			resume_stack.Pop();
			OperGotoContinue();
		}

		/// <summary> OP_FINALLY </summary>
		void OperFinally()
		{
			n++;
		}

		/// <summary> OP_DISCARD_ERROR </summary>
		void OperDiscardError()
		{
			scripter.DiscardError();
			n++;
		}

		/// <summary> OP_EXIT_ON_ERROR </summary>
		void OperExitOnError()
		{
			if (scripter.IsError())
				RaiseError();
			else
				n++;
		}

		/// <summary> Raises error </summary>
		void RaiseError()
		{
			while (stack.Count > curr_stack_count)
				stack.Pop();


			if (try_stack.Count == 0)
			{
				if (PascalOrBasic(n))
				{
					int i = n;
					resume_stack.Push(n);

					while (this[i].op != OP_INIT_METHOD) --i;
					for (;;)
					{
						i++;
						if (this[i].op == OP_ONERROR)
						{
							n = i + 1;
							return;
						}

						if (this[i].op == OP_END_METHOD)
							break;
					}
				}

				Terminated = true;
				Paused = false;
				return;
			}

			Again:

			r = (ProgRec) prog[n];
			int op = r.op;

			for (;;)
			{
				if ((op == OP_RET) ||
					(op == OP_FINALLY) ||
					(op == OP_CATCH) ||
					(op == OP_HALT))
					break;
				if (op == OP_TRY_ON)
					OperTryOn();
				else if (op == OP_TRY_OFF)
					OperTryOff();
				else if (op == OP_HALT)
					OperHalt();
				else
					n++;
				r = (ProgRec) prog[n];
				op = r.op;
			}

			if (op == OP_RET)
			{
				OperRet();
				goto Again;
			}
			else if (op == OP_CATCH)
			{
				if (try_stack.Legal(n))
				{
					if (r.arg1 > 0)
					{
						Type t = scripter.LastError.E.GetType();
						int curr_class_id = symbol_table.RegisterType(t, false);
						int class_id = symbol_table[r.arg1].TypeId;

                        if (class_id == 0)
                            return;

						if (class_id == curr_class_id)
							PutValue(r.arg1, scripter.LastError.E);
						else
						{
							ClassObject curr = GetClassObject(curr_class_id);
							ClassObject c = GetClassObject(class_id);
							if (curr.InheritsFrom(c) || c == curr)
								PutValue(r.arg1, scripter.LastError.E);
							else
							{
								int i = custom_ex_list.IndexOf(scripter.LastError.E);
								if (i >= 0)
								{
									ObjectObject v = custom_ex_list.Objects[i] as ObjectObject;
									curr = v.Class_Object;
									if (curr.InheritsFrom(c) || (c == curr))
									{
										PutValue(r.arg1, v);
										return;
									}
								}
								n++;
								goto Again;
							}
						}
					}
					else
					{
						n++;
						return;
						// ok
					}
				}
				else
				{
					n++;
					goto Again;
				}
			}
			else if (op == OP_FINALLY)
			{
				if(!try_stack.Legal(n))
					goto Again;
			}
		}

		/// <summary> OP_ASSIGN </summary>
		void OperAssign()
		{
			object v = null;
			if (symbol_table[r.res].is_static)
			{
				ProgRec q = this[n + 1];
				if (q.op == OP_INIT_STATIC_VAR && q.arg1 == r.res)
				{
					if (q.res == 0)
					{
						q.res = 1;
						v = GetValue(r.arg2);
						PutValue(r.res, v);
					}
				}
				else
				{
					v = GetValue(r.arg2);
					PutValue(r.res, v);
				}
			}
			else
			{
				v = GetValue(r.arg2);
				PutValue(r.res, v);
			}

			n++;
		}

		/// <summary> OP_ASSIGN_STRUCT </summary>
		void OperAssignStruct()
		{
			ObjectObject v = scripter.ToScriptObject(GetValue(r.arg2));
			PutValue(r.res, v.Clone());
			n++;
		}

		/// <summary> OP_PLUS </summary>
		void OperPlus()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 + (int)v2);
			n++;
		}

		/// <summary> OP_MINUS </summary>
		void OperMinus()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 - (int)v2);
			n++;
		}

		/// <summary> OP_MULT </summary>
		void OperMult()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 * (int)v2);
			n++;
		}

		/// <summary> OP_EXP </summary>
		void OperExp()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 * (int)v2);
			n++;
		}

		/// <summary> OP_DIV </summary>
		void OperDiv()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 / (int)v2);
			n++;
		}

		/// <summary> OP_EQ </summary>
		void OperEq()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, v1 == v2);
			n++;
		}

		/// <summary> OP_NE </summary>
		void OperNe()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, v1 != v2);
			n++;
		}

		/// <summary> OP_GT </summary>
		void OperGt()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 > (int)v2);
			n++;
		}

		/// <summary> OP_GE </summary>
		void OperGe()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 >= (int)v2);
			n++;
		}

		/// <summary> OP_LT </summary>
		void OperLt()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 < (int)v2);
			n++;
		}

		/// <summary> OP_LE </summary>
		void OperLe()
		{
			object v1 = GetValue(r.arg1);
			object v2 = GetValue(r.arg2);
			PutValue(r.res, (int)v1 <= (int)v2);
			n++;
		}

		/// <summary> OP_IS </summary>
		void OperIs()
		{
			object v1 = GetValue(r.arg1);
			if (v1 == null)
				PutValue(r.res, false);
			else
			{
				ObjectObject o1 = scripter.ToScriptObject(v1);
				if (o1.Class_Object.Id == r.arg2)
					PutValue(r.res, true);
				else if (o1.Class_Object.IsValueType)
				{
					ClassObject c1 = o1.Class_Object;
					ClassObject c2 = GetClassObject(r.arg2);

					if (c2.Class_Kind == ClassKind.Interface)
					{
						bool ok = c1.Implements(c2);
						PutValue(r.res, ok);
					}
					else
					{
						bool ok = c1.InheritsFrom(c2);
						PutValue(r.res, ok);

//						PutValue(r.res, false);
					}
				}
				else // reference type
				{
					ClassObject c1 = o1.Class_Object;
					ClassObject c2 = GetClassObject(r.arg2);
					bool ok = c1.InheritsFrom(c2);
					PutValue(r.res, ok);
				}
			}
			n++;
		}

		/// <summary> OP_AS </summary>
		void OperAs()
		{
			object v1 = GetValue(r.arg1);
			if (v1 == null)
				PutValue(r.res, null);
			else
			{
				ObjectObject o1 = scripter.ToScriptObject(v1);
				if (o1.Class_Object.Id == r.arg2)
					PutValue(r.res, v1);
				else if (o1.Class_Object.IsValueType)
				{
					ClassObject c1 = o1.Class_Object;
					ClassObject c2 = GetClassObject(r.arg2);
					if (c2.Class_Kind == ClassKind.Interface)
					{
						if (c1.Implements(c2))
							PutVal(r.res, v1);
						else
							PutValue(r.res, null);
					}
					else
						PutValue(r.res, null);
				}
				else // reference type
				{
					ClassObject c1 = o1.Class_Object;
					ClassObject c2 = GetClassObject(r.arg2);
					if (c2.InheritsFrom(c1))
						PutVal(r.res, v1);
					else
						PutValue(r.res, null);
				}
			}
			n++;
		}

		/// <summary> OP_HALT </summary>
		void OperHalt()
		{
			Terminated = true;
		}

		/// <summary> OP_PRINT </summary>
		void OperPrint()
		{
#if !PORTABLE
			object v1 = GetValue(r.arg1);
			if (v1 == null)
				Console.Write("null");
			else
			{
				string s = "";
#if full
				int t = symbol_table[r.arg1].TypeId;
				ClassObject c = GetClassObject(t);
				if (c.Class_Kind == ClassKind.Enum)
				{
					int vi = (int) Conversion.ChangeType(v1, typeof(int));
					s = Enum.GetName(c.RType, vi);
					if (s == null)
						s = v1.ToString();
				}
				else
#endif
				{
					s = v1.ToString();
				}
				Console.Write(s);
			}
#endif
			n++;
		}

		/// <summary> Main run loop </summary>
		public void Run(RunMode run_mode)
		{
            scripter.Owner.RunCount++;

            try
            {
                Terminated = false;
                Paused = false;

                n++;

                bool fastrun = !debugging ||
                    (run_mode == RunMode.Run && breakpoint_list.Count == 0);

                if (!fastrun)
                {
                    int next_line = NextLine();
                    int stack_count = stack.Count;
                    int sub_id = callstack.CurrSubId;

                    breakpoint_list.Activate();

                    do
                    {
                        if (scripter.TerminatedFlag)
                            return;

                        for (; ; )
                        {

                            r = (ProgRec)prog[n];
                            if (r.op != OP_SEPARATOR)
                                break;
                            if (HasBreakpoint(n))
                            {
                                Paused = true;
                                return;
                            }
                            switch (run_mode)
                            {
                                case RunMode.Run:
                                    n++;
                                    break;
                                case RunMode.TraceInto:
                                    {
                                        while (this[n + 1].op == OP_SEPARATOR) n++;
                                        Paused = true;
                                        return;
                                    }
                                case RunMode.StepOver:
                                    if (stack.Count <= stack_count)
                                    {
                                        while (this[n + 1].op == OP_SEPARATOR) n++;
                                        Paused = true;
                                        return;
                                    }
                                    n++;
                                    break;
                                case RunMode.NextLine:
                                    if (n == next_line)
                                    {
                                        while (this[n + 1].op == OP_SEPARATOR) n++;
                                        Paused = true;
                                        return;
                                    }
                                    n++;
                                    break;
                                case RunMode.UntilReturn:
                                    if (!callstack.HasSubId(sub_id))
                                    {
                                        while (this[n + 1].op == OP_SEPARATOR) n++;
                                        Paused = true;
                                        return;
                                    }
                                    n++;
                                    break;
                            }
                        }

                        if (scripter.Owner.rh != null)
                            scripter.Owner.rh(scripter.Owner);

                        try
                        {
                            r = (ProgRec)prog[n];
                            Oper Oper = (Oper)arrProc[-r.op];

                            if (Checked)
                                Oper();
                            else
                                unchecked { Oper(); };

                        }
                        catch (Exception e)
                        {
                            if (scripter.Owner.re != null)
                                scripter.Owner.re(scripter.Owner, e);

                            scripter.Dump();
                            scripter.Error_List.Add(new ScriptError(scripter, e.Message));
                            scripter.LastError.E = e;

                            if (e.InnerException != null)
                            {
                                e = e.InnerException;
                                if (e != null)
                                {
                                    scripter.Error_List.Add(new ScriptError(scripter, e.Message));
                                    scripter.LastError.E = e;
                                }
                            }

                            RaiseError();
                        }
                    }
                    while (!Terminated);
                }
                else
                {
                    do
                    {
                        if (scripter.TerminatedFlag)
                            return;

                        if (scripter.Owner.rh != null)
                            scripter.Owner.rh(scripter.Owner);

                        try
                        {
                            r = (ProgRec)prog[n];
                            Oper Oper = (Oper)arrProc[-r.op];
                            if (Checked)
                                Oper();
                            else
                                unchecked { Oper(); };
                        }
                        catch (Exception e)
                        {
                            if (scripter.Owner.re != null)
                                scripter.Owner.re(scripter.Owner, e);

                            scripter.Dump();
                            scripter.Error_List.Add(new ScriptError(scripter, e.Message));
                            scripter.LastError.E = e;

                            if (e.InnerException != null)
                            {
                                e = e.InnerException;
                                if (e != null)
                                {
                                    scripter.Error_List.Add(new ScriptError(scripter, e.Message));
                                    scripter.LastError.E = e;
                                }
                            }

                            RaiseError();
                        }

                    }
                    while (!Terminated);
                }
            }

            finally
            {
                scripter.Owner.RunCount--; // October 26, 2007
            }
		}

		/// <summary> Calls script-defind method </summary>
		public object CallMethodEx(RunMode rm, object target, int sub_id, params object[] p)
		{
			object result = null;

			SaveState();

			int param_count = 0;
			int res = symbol_table.GetResultId(sub_id);

			n = Card;

			if (p != null)
			{
				param_count = p.Length;
				// push parameters
				for (int i = 0; i < param_count; i++)
				{
					stack.Push(p[i]);
				}
			}

			stack.Push(target); // object

			Card ++;
			SetInstruction(Card, OP_CALL, sub_id, param_count, res);
			Card ++;
			SetInstruction(Card, OP_HALT, 0, 0, 0);

			Run(rm);

			result = symbol_table[res].Value;

//			RestoreState();
			if (Paused)
			{
				int old_n = n;
				RestoreState();
				n = old_n;
			}
			else
				RestoreState();


			return result;
		}

		/// <summary> Calls script-defind method </summary>
		public object CallMethod(RunMode rm, object target, int sub_id, params object[] p)
		{
			object result = null;

			if (sub_id != scripter.EntryId)
				SaveState();

			int param_count = 0;
			int res = symbol_table.GetResultId(sub_id);

			FunctionObject f = GetFunctionObject(sub_id);

			n = Card;

			if ((p != null) && (f.ParamCount > 0))
			{
//				param_count = 1;
//				stack.Push(p);

				param_count = p.Length;
				// push parameters
				for (int i = 0; i < param_count; i++)
				{
					stack.Push(p[i]);
				}

			}

			stack.Push(target); // object

			Card ++;
			SetInstruction(Card, OP_CALL, sub_id, param_count, res);
			Card ++;
			SetInstruction(Card, OP_HALT, sub_id, 0, 0);

			Run(rm);

			result = symbol_table[res].Value;

			if (sub_id != scripter.EntryId)
				RestoreState();

			return result;
		}

		/// <summary>
		/// Returns 'true', p-code has breakpoint at line [i].
		/// </summary>
		public bool HasBreakpoint(int i)
		{
			return breakpoint_list.HasBreakpoint(i);
		}

		/// <summary>
		/// Adds breakpoint.
		/// </summary>
		public void AddBreakpoint(Breakpoint bp)
		{
			breakpoint_list.Add(bp);
		}

		/// <summary>
		/// Removes breakpoint.
		/// </summary>
		public void RemoveBreakpoint(Breakpoint bp)
		{
			breakpoint_list.Remove(bp);
		}

		/// <summary>
		/// Removes breakpoint.
		/// </summary>
		public void RemoveBreakpoint(string module_name, int line_number)
		{
			breakpoint_list.Remove(module_name, line_number);
		}

		/// <summary>
		/// Removes all breakpoints.
		/// </summary>
		public void RemoveAllBreakpoints()
		{
			breakpoint_list.Clear();
		}

		/// <summary>
		/// Returns breakpoint list.
		/// </summary>
		public BreakpointList Breakpoints
		{
			get
			{
				return breakpoint_list;
			}
		}

		/// <summary>
		/// Returns call stack.
		/// </summary>
		public CallStack Call_Stack
		{
			get
			{
				return callstack;
			}
		}

		/// <summary>
		/// Returns next line of p-code.
		/// </summary>
		public int NextLine()
		{
			if (n > Card || n < 1)
				return -1;
			int i = n;
			for (;;)
			{
				if (this[i].op == OP_SEPARATOR)
					return i;
				if (i > Card)
					return -1;
				i++;
			}
		}

		/// <summary>
		/// Returns source line number.
		/// </summary>
		public int GetLineNumber(int j)
		{
			int i = j;

			for (;;)
			{
				if (i <= 0)
					return -1;
				if (this[i].op == OP_SEPARATOR)
					return this[i].arg2;
				else
					i--;
			}
		}

		/// <summary>
		/// Returns source line number.
		/// </summary>
		public int GetErrorLineNumber(int j)
		{
			bool b = this[j].op == OP_SEPARATOR;;
			
			int i = j;

			for (;;)
			{
				if (i <= 0)
					return -1;
				if (this[i].op == OP_SEPARATOR)
				{
					if (b && i > 1 && this[i-1].op == OP_SEPARATOR)
						i--;
					else
					{
						if (b)
							return this[i].arg2 - 1;
						else
							return this[i].arg2;
					}
				}
				else
					i--;
			}
		}

		/// <summary>
		/// Returns current source line number.
		/// </summary>
		public int CurrentLineNumber
		{
			get
			{
				int pcode_line = n;
				if (pcode_line == 0)
					pcode_line = Card;

				return GetLineNumber(pcode_line);
			}
		}

		/// <summary>
		/// Returns current source line.
		/// </summary>
		public string CurrentLine
		{
			get
			{
				int pcode_line = n;
				if (pcode_line == 0)
					pcode_line = Card;

				Module m = GetModule(pcode_line);
				if (m == null)
					return "";
				int line_number = GetLineNumber(pcode_line);
				if (line_number == -1)
					return "";
				return m.GetLine(line_number);
			}
		}

		/// <summary>
		/// Returns current module.
		/// </summary>
		public string CurrentModule
		{
			get
			{
				int pcode_line = n;
				if (pcode_line == 0)
					pcode_line = Card;

				Module m = GetModule(pcode_line);
				if (m == null)
					return "";
				else
					return m.Name;
			}
		}

		/// <summary>
		/// Returns module.
		/// </summary>
		public Module GetModule(int j)
		{
			ProgRec	r;
			int i = j;
			do
			{
				r = (ProgRec) prog[i];
				if (r.op == OP_BEGIN_MODULE)
					return scripter.module_list.GetModule(r.arg1);
				else
					i--;

			} while (i > 0);
			return scripter.module_list.GetModule(r.arg1);
		}

		/// <summary>
		/// Returns language.
		/// </summary>
		public PaxLanguage GetLanguage(int j)
		{
			int i = j;
			for (;;)
			{
				if (this[i].op == OP_BEGIN_MODULE)
					return (PaxLanguage) this[i].arg2;
				else
					i--;

			}
		}

		/// <summary>
		/// Returns id of method at p-code line [j].
		/// </summary>
		public int GetSubId(int j)
		{
			ProgRec	r;
			int i = j;
			do
			{
				r = (ProgRec) prog[i];
				if (r.op == OP_CREATE_METHOD)
					return r.arg1;
				else
					i--;

			} while (i > 0);
			return -1;
		}

		/// <summary>
		/// Recreates stack of levels at p-code line [init_n].
		/// </summary>
		public IntegerStack RecreateLevelStack(int init_n)
		{
			Module m = GetModule(init_n);
			if (m == null)
				return null;
			IntegerStack l = new IntegerStack();

			for (int i = m.P1; i <= m.P2; i++)
			{
				if (this[i].op == OP_CREATE_METHOD)
					l.Push(this[i].arg1);
				else if (this[i].op == OP_END_METHOD)
					l.Pop();
				else if (this[i].op == OP_BEGIN_USING)
					l.Push(this[i].arg1);
				else if (this[i].op == OP_END_USING)
					l.Pop();

				if (i == init_n)
					return l;
			}
			return l;
		}

		/// <summary>
		/// Recreates class stack at p-code line [init_n].
		/// </summary>
		public IntegerStack RecreateClassStack(int init_n)
		{
			Module m = GetModule(init_n);
			if (m == null)
				return null;
			IntegerStack l = new IntegerStack();

			for (int i = m.P1; i <= m.P2; i++)
			{
				if (this[i].op == OP_CREATE_CLASS)
					l.Push(this[i].arg1);
				else if (this[i].op == OP_END_CLASS)
					l.Pop();

				if (i == init_n)
					return l;
			}
			return l;
		}

		/// <summary>
		/// Returns total number of p-code lines.
		/// </summary>
		public int Card
		{
			get
			{
				return card;
			}
			set
			{
				while (value >= prog.Count)
				{
					for (int i = 0; i < DELTA_PROG_CARD; i++)
						prog.Add(new ProgRec());
				}
				card = value;
			}
		}

		/// <summary>
		/// Returns p-code instruction at line [i].
		/// </summary>
		public ProgRec this[int i]
		{
			get
			{
				return (ProgRec) prog[i];
			}
		}

		/// <summary>
		/// Returns id of System.Object.
		/// </summary>
		public int ObjectTypeId
		{
			get
			{
				return symbol_table.OBJECT_CLASS_id;
			}
		}

		// DETAILED UNARY MINUS OPERATORS ********************************

		/// <summary> OP_NEGATION_INT </summary>
		void OperNegationInt()
		{
			PutValue(r.res, - GetValueAsInt(r.arg1));
			n++;
		}

		/// <summary> OP_NEGATION_LONG </summary>
		void OperNegationLong()
		{
			PutValueAsLong(r.res, - GetValueAsLong(r.arg1));
			n++;
		}

		/// <summary> OP_NEGATION_FLOAT </summary>
		void OperNegationFloat()
		{
			PutValueAsFloat(r.res, - GetValueAsFloat(r.arg1));
			n++;
		}

		/// <summary> OP_NEGATION_DOUBLE </summary>
		void OperNegationDouble()
		{
			PutValueAsDouble(r.res, - GetValueAsDouble(r.arg1));
			n++;
		}

		/// <summary> OP_NEGATION_DECIMAL </summary>
		void OperNegationDecimal()
		{
			PutValueAsDecimal(r.res, - GetValueAsDecimal(r.arg1));
			n++;
		}

		// DETAILED LOGICAL NEGATION OPERATORS ***************************

		/// <summary> OP_NEGATION_BOOL </summary>
		void OperLogicalNegationBool()
		{
			PutValue(r.res, ! GetValueAsBool(r.arg1));
			n++;
		}

		// DETAILED BITWISE COMPLEMENT OPERATORS *************************

		/// <summary> OP_BITWISE_COMPLEMENT_INT </summary>
		void OperBitwiseComplementInt()
		{
			PutValue(r.res, ~ GetValueAsInt(r.arg1));
			n++;
		}

		/// <summary> OP_BITWISE_COMPLEMENT_UINT </summary>
		void OperBitwiseComplementUint()
		{
			PutValue(r.res, ~ (uint)GetValueAsInt(r.arg1));
			n++;
		}

		/// <summary> OP_BITWISE_COMPLEMENT_LONG </summary>
		void OperBitwiseComplementLong()
		{
			PutValueAsLong(r.res, ~ GetValueAsLong(r.arg1));
			n++;
		}

		/// <summary> OP_BITWISE_COMPLEMENT_ULONG </summary>
		void OperBitwiseComplementUlong()
		{
			PutValue(r.res, ~ (ulong)GetValueAsLong(r.arg1));
			n++;
		}

		// DETAILED INC OPERATORS ****************************************

		/// <summary> OP_INC_SBYTE </summary>
		void OperIncSbyte()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			sbyte v = (sbyte) Conversion.ChangeType(value, typeof(sbyte));
			PutValue(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_BYTE </summary>
		void OperIncByte()
		{
			byte v = GetValueAsByte(r.arg1);
			PutValue(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_USHORT </summary>
		void OperIncUshort()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			ushort v = (ushort) Conversion.ChangeType(value, typeof(ushort));
			PutValue(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_SHORT </summary>
		void OperIncShort()
		{
			short v = GetValueAsShort(r.arg1);
			PutValue(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_INT </summary>
		void OperIncInt()
		{
			int v = GetValueAsInt(r.arg1);
			PutValueAsInt(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_UINT </summary>
		void OperIncUint()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			uint v = (uint) Conversion.ChangeType(value, typeof(uint));
			PutValue(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_LONG </summary>
		void OperIncLong()
		{
			long v = GetValueAsLong(r.arg1);
			PutValueAsLong(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_ULONG </summary>
		void OperIncUlong()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			ulong v = (ulong) Conversion.ChangeType(value, typeof(ulong));
			PutValue(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_CHAR </summary>
		void OperIncChar()
		{
			char v = GetValueAsChar(r.arg1);
			PutValue(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_FLOAT </summary>
		void OperIncFloat()
		{
			float v = GetValueAsFloat(r.arg1);
			PutValueAsFloat(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_DOUBLE </summary>
		void OperIncDouble()
		{
			double v = GetValueAsDouble(r.arg1);
			PutValueAsDouble(r.res, ++v);
			n++;
		}

		/// <summary> OP_INC_DECIMAL </summary>
		void OperIncDecimal()
		{
			decimal v = GetValueAsDecimal(r.arg1);
			PutValueAsDecimal(r.res, ++v);
			n++;
		}

		// DETAILED DEC OPERATORS ***************************************

		/// <summary> OP_DEC_SBYTE </summary>
		void OperDecSbyte()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			sbyte v = (sbyte) Conversion.ChangeType(value, typeof(sbyte));
			PutValue(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_BYTE </summary>
		void OperDecByte()
		{
			byte v = GetValueAsByte(r.arg1);
			PutValue(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_USHORT </summary>
		void OperDecUshort()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			ushort v = (ushort) Conversion.ChangeType(value, typeof(ushort));
			PutValue(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_SHORT </summary>
		void OperDecShort()
		{
			short v = GetValueAsShort(r.arg1);
			PutValue(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_INT </summary>
		void OperDecInt()
		{
			int v = GetValueAsInt(r.arg1);
			PutValueAsInt(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_UINT </summary>
		void OperDecUint()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			uint v = (uint) Conversion.ChangeType(value, typeof(uint));
			PutValue(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_LONG </summary>
		void OperDecLong()
		{
			long v = GetValueAsLong(r.arg1);
			PutValueAsLong(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_ULONG </summary>
		void OperDecUlong()
		{
			object value = Conversion.ToPrimitive(GetValue(r.arg1));
			ulong v = (ulong) Conversion.ChangeType(value, typeof(ulong));
			PutValue(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_CHAR </summary>
		void OperDecChar()
		{
			char v = GetValueAsChar(r.arg1);
			PutValue(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_FLOAT </summary>
		void OperDecFloat()
		{
			float v = GetValueAsFloat(r.arg1);
			PutValueAsFloat(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_DOUBLE </summary>
		void OperDecDouble()
		{
			double v = GetValueAsDouble(r.arg1);
			PutValueAsDouble(r.res, --v);
			n++;
		}

		/// <summary> OP_DEC_DECIMAL </summary>
		void OperDecDecimal()
		{
			decimal v = GetValueAsDecimal(r.arg1);
			PutValueAsDecimal(r.res, --v);
			n++;
		}

		// DETAILED ADDITION OPERATORS *********************************

		/// <summary> OP_ADDITION_INT </summary>
		void OperAdditionInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) + GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_ADDITION_UINT </summary>
		void OperAdditionUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) + (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_ADDITION_LONG </summary>
		void OperAdditionLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) + GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_ADDITION_ULONG </summary>
		void OperAdditionUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) + (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_ADDITION_FLOAT </summary>
		void OperAdditionFloat()
		{
			PutValueAsFloat(r.res, GetValueAsFloat(r.arg1) + GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_ADDITION_DOUBLE </summary>
		void OperAdditionDouble()
		{
			PutValueAsDouble(r.res, GetValueAsDouble(r.arg1) + GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_ADDITION_DECIMAL </summary>
		void OperAdditionDecimal()
		{
			PutValueAsDecimal(r.res, GetValueAsDecimal(r.arg1) + GetValueAsDecimal(r.arg2));
			n++;
		}

		/// <summary> OP_ADDITION_STRING </summary>
		void OperAdditionString()
		{
			PutValueAsString(r.res, GetValueAsString(r.arg1) + GetValueAsString(r.arg2));
			n++;
		}

		// DETAILED SUBTRACTION OPERATORS ********************************

		/// <summary> OP_SUBTRACTION_INT </summary>
		void OperSubtractionInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) - GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_SUBTRACTION_UINT </summary>
		void OperSubtractionUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) - (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_SUBTRACTION_LONG </summary>
		void OperSubtractionLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) - GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_SUBTRACTION_ULONG </summary>
		void OperSubtractionUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) - (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_SUBTRACTION_FLOAT </summary>
		void OperSubtractionFloat()
		{
			PutValueAsFloat(r.res, GetValueAsFloat(r.arg1) - GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_SUBTRACTION_DOUBLE </summary>
		void OperSubtractionDouble()
		{
			PutValueAsDouble(r.res, GetValueAsDouble(r.arg1) - GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_SUBTRACTION_DECIMAL </summary>
		void OperSubtractionDecimal()
		{
			PutValueAsDecimal(r.res, GetValueAsDecimal(r.arg1) - GetValueAsDecimal(r.arg2));
			n++;
		}

		// DETAILED MULTIPLICATION OPERATORS *****************************

		/// <summary> OP_MULTIPLICATION_INT </summary>
		void OperMultiplicationInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) * GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_MULTIPLICATION_UINT </summary>
		void OperMultiplicationUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) * (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_MULTIPLICATION_LONG </summary>
		void OperMultiplicationLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) * GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_MULTIPLICATION_ULONG </summary>
		void OperMultiplicationUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) * (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_MULTIPLICATION_FLOAT </summary>
		void OperMultiplicationFloat()
		{
			PutValueAsFloat(r.res, GetValueAsFloat(r.arg1) * GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_MULTIPLICATION_DOUBLE </summary>
		void OperMultiplicationDouble()
		{
			PutValueAsDouble(r.res, GetValueAsDouble(r.arg1) * GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_MULTIPLICATION_DECIMAL </summary>
		void OperMultiplicationDecimal()
		{
			PutValueAsDecimal(r.res, GetValueAsDecimal(r.arg1) * GetValueAsDecimal(r.arg2));
			n++;
		}


		// DETAILED EXPONENT OPERATORS *****************************

		/// <summary> OP_EXPONENT_INT </summary>
		void OperExponentInt()
		{
			int k = GetValueAsInt(r.arg2);

			int v = GetValueAsInt(r.arg1);
			int s = v;

			for (int i=1; i <= k - 1; i++)
				s = s * v;

			PutValueAsInt(r.res, s);
			n++;
		}

		/// <summary> OP_EXPONENT_UINT </summary>
		void OperExponentUint()
		{
			int k = GetValueAsInt(r.arg2);

			uint v = (uint) GetValueAsInt(r.arg1);
			uint s = v;

			for (int i=1; i <= k - 1; i++)
				s = s * v;

			PutValue(r.res, s);
			n++;
		}

		/// <summary> OP_EXPONENT_LONG </summary>
		void OperExponentLong()
		{
			int k = GetValueAsInt(r.arg2);

			long v = GetValueAsLong(r.arg1);
			long s = v;

			for (int i=1; i <= k - 1; i++)
				s = s * v;

			PutValueAsLong(r.res, s);
			n++;
		}

		/// <summary> OP_EXPONENT_ULONG </summary>
		void OperExponentUlong()
		{
			int k = GetValueAsInt(r.arg2);

			ulong v = (ulong) GetValueAsLong(r.arg1);
			ulong s = v;

			for (int i=1; i <= k - 1; i++)
				s = s * v;

			PutValue(r.res, s);
			n++;
		}

		/// <summary> OP_EXPONENT_FLOAT </summary>
		void OperExponentFloat()
		{
			int k = GetValueAsInt(r.arg2);

			float v = GetValueAsFloat(r.arg1);
			float s = v;

			for (int i=1; i <= k - 1; i++)
				s = s * v;

			PutValueAsFloat(r.res, s);
			n++;
		}

		/// <summary> OP_EXPONENT_DOUBLE </summary>
		void OperExponentDouble()
		{
			int k = GetValueAsInt(r.arg2);

			double v = GetValueAsDouble(r.arg1);
			double s = v;

			for (int i=1; i <= k - 1; i++)
				s = s * v;

			PutValueAsDouble(r.res, s);
			n++;
		}

		/// <summary> OP_EXPONENT_DECIMAL </summary>
		void OperExponentDecimal()
		{
			int k = GetValueAsInt(r.arg2);

			decimal v = GetValueAsDecimal(r.arg1);
			decimal s = v;

			for (int i=1; i <= k - 1; i++)
				s = s * v;

			PutValueAsDecimal(r.res, s);
			n++;
		}

		// DETAILED DIVISION OPERATORS ***********************************

		/// <summary> OP_DIVISION_INT </summary>
		void OperDivisionInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) / GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_DIVISION_UINT </summary>
		void OperDivisionUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) / (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_DIVISION_LONG </summary>
		void OperDivisionLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) / GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_DIVISION_ULONG </summary>
		void OperDivisionUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) / (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_DIVISION_FLOAT </summary>
		void OperDivisionFloat()
		{
			PutValueAsFloat(r.res, GetValueAsFloat(r.arg1) / GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_DIVISION_DOUBLE </summary>
		void OperDivisionDouble()
		{
			PutValueAsDouble(r.res, GetValueAsDouble(r.arg1) / GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_DIVISION_DECIMAL </summary>
		void OperDivisionDecimal()
		{
			PutValueAsDecimal(r.res, GetValueAsDecimal(r.arg1) / GetValueAsDecimal(r.arg2));
			n++;
		}

		// DETAILED REMAINDER OPERATORS **********************************

		/// <summary> OP_REMAINDER_INT </summary>
		void OperRemainderInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) % GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_REMAINDER_UINT </summary>
		void OperRemainderUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) % (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_REMAINDER_LONG </summary>
		void OperRemainderLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) % GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_REMAINDER_ULONG </summary>
		void OperRemainderUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) % (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_REMAINDER_FLOAT </summary>
		void OperRemainderFloat()
		{
			PutValueAsFloat(r.res, GetValueAsFloat(r.arg1) % GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_REMAINDER_DOUBLE </summary>
		void OperRemainderDouble()
		{
			PutValueAsDouble(r.res, GetValueAsDouble(r.arg1) % GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_REMAINDER_DECIMAL </summary>
		void OperRemainderDecimal()
		{
			PutValueAsDecimal(r.res, GetValueAsDecimal(r.arg1) % GetValueAsDecimal(r.arg2));
			n++;
		}

		// DETAILED LEFT SHIFT OPERATORS *********************************

		/// <summary> OP_LEFT_SHIFT_INT </summary>
		void OperLeftShiftInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) << GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_LEFT_SHIFT_UINT </summary>
		void OperLeftShiftUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) << GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_LEFT_SHIFT_LONG </summary>
		void OperLeftShiftLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) << GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_LEFT_SHIFT_ULONG </summary>
		void OperLeftShiftUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) << GetValueAsInt(r.arg2));
			n++;
		}

		// DETAILED RIGHT SHIFT OPERATORS ********************************

		/// <summary> OP_RIGHT_SHIFT_INT </summary>
		void OperRightShiftInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) >> GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_RIGHT_SHIFT_UINT </summary>
		void OperRightShiftUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) >> GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_RIGHT_SHIFT_LONG </summary>
		void OperRightShiftLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) >> GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_RIGHT_SHIFT_ULONG </summary>
		void OperRightShiftUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) >> GetValueAsInt(r.arg2));
			n++;
		}

		// DETAILED BITWISE AND OPERATORS ********************************

		/// <summary> OP_BITWISE_AND_INT </summary>
		void OperBitwiseAndInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) & GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_AND_UINT </summary>
		void OperBitwiseAndUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) & (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_AND_LONG </summary>
		void OperBitwiseAndLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) & GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_AND_ULONG </summary>
		void OperBitwiseAndUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) & (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_AND_BOOL </summary>
		void OperBitwiseAndBool()
		{
			PutValue(r.res, GetValueAsBool(r.arg1) & GetValueAsBool(r.arg2));
			n++;
		}

		// DETAILED BITWISE OR OPERATORS *********************************

		/// <summary> OP_BITWISE_OR_INT </summary>
		void OperBitwiseOrInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) | GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_OR_UINT </summary>
		void OperBitwiseOrUint()
		{
			PutValue(r.res, (uint)GetValue(r.arg1) | (uint)GetValue(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_OR_LONG </summary>
		void OperBitwiseOrLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) | GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_OR_ULONG </summary>
		void OperBitwiseOrUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) | (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_OR_BOOL </summary>
		void OperBitwiseOrBool()
		{
			PutValue(r.res, GetValueAsBool(r.arg1) | GetValueAsBool(r.arg2));
			n++;
		}

		// DETAILED BITWISE XOR OPERATORS ********************************

		/// <summary> OP_BITWISE_XOR_INT </summary>
		void OperBitwiseXorInt()
		{
			PutValueAsInt(r.res, GetValueAsInt(r.arg1) ^ GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_XOR_UINT </summary>
		void OperBitwiseXorUint()
		{
			PutValue(r.res, (uint)GetValueAsInt(r.arg1) ^ (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_XOR_LONG </summary>
		void OperBitwiseXorLong()
		{
			PutValueAsLong(r.res, GetValueAsLong(r.arg1) ^ GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_XOR_ULONG </summary>
		void OperBitwiseXorUlong()
		{
			PutValue(r.res, (ulong)GetValueAsLong(r.arg1) ^ (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_BITWISE_XOR_BOOL </summary>
		void OperBitwiseXorBool()
		{
			PutValue(r.res, GetValueAsBool(r.arg1) ^ GetValueAsBool(r.arg2));
			n++;
		}

		// DETAILED LT OPERATORS *****************************************

		/// <summary> OP_LT_INT </summary>
		void OperLessThanInt()
		{
			PutValueAsBool(r.res, GetValueAsInt(r.arg1) < GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_LT_UINT </summary>
		void OperLessThanUint()
		{
			PutValueAsBool(r.res, (uint)GetValueAsInt(r.arg1) < (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_LT_LONG </summary>
		void OperLessThanLong()
		{
			PutValueAsBool(r.res, GetValueAsLong(r.arg1) < GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_LT_ULONG </summary>
		void OperLessThanUlong()
		{
			PutValueAsBool(r.res, (ulong)GetValueAsLong(r.arg1) < (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_LT_FLOAT </summary>
		void OperLessThanFloat()
		{
			PutValueAsBool(r.res, GetValueAsFloat(r.arg1) < GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_LT_DOUBLE </summary>
		void OperLessThanDouble()
		{
			PutValueAsBool(r.res, GetValueAsDouble(r.arg1) < GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_LT_DECIMAL </summary>
		void OperLessThanDecimal()
		{
			PutValueAsBool(r.res, GetValueAsDecimal(r.arg1) < GetValueAsDecimal(r.arg2));
			n++;
		}

		/// <summary> OP_LT_STRING </summary>
		void OperLessThanString()
		{
			int z = String.Compare(GetValueAsString(r.arg1), GetValueAsString(r.arg2));
			PutValueAsBool(r.res, z < 0);
			n++;
		}

		// DETAILED LE OPERATORS *****************************************

		/// <summary> OP_LE_INT </summary>
		void OperLessThanOrEqualInt()
		{
			PutValueAsBool(r.res, GetValueAsInt(r.arg1) <= GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_LE_UINT </summary>
		void OperLessThanOrEqualUint()
		{
			PutValueAsBool(r.res, (uint)GetValueAsInt(r.arg1) <= (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_LE_LONG </summary>
		void OperLessThanOrEqualLong()
		{
			PutValueAsBool(r.res, GetValueAsLong(r.arg1) <= GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_LE_ULONG </summary>
		void OperLessThanOrEqualUlong()
		{
			PutValueAsBool(r.res, (ulong)GetValueAsLong(r.arg1) <= (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_LE_FLOAT </summary>
		void OperLessThanOrEqualFloat()
		{
			PutValueAsBool(r.res, GetValueAsFloat(r.arg1) <= GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_LE_DOUBLE </summary>
		void OperLessThanOrEqualDouble()
		{
			PutValueAsBool(r.res, GetValueAsDouble(r.arg1) <= GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_LE_DECIMAL </summary>
		void OperLessThanOrEqualDecimal()
		{
			PutValueAsBool(r.res, GetValueAsDecimal(r.arg1) <= GetValueAsDecimal(r.arg2));
			n++;
		}

		/// <summary> OP_LE_STRING </summary>
		void OperLessThanOrEqualString()
		{
			int z = String.Compare(GetValueAsString(r.arg1), GetValueAsString(r.arg2));
			PutValueAsBool(r.res, z <= 0);
			n++;
		}

		// DETAILED GT OPERATORS *****************************************

		/// <summary> OP_GT_INT </summary>
		void OperGreaterThanInt()
		{
			PutValueAsBool(r.res, GetValueAsInt(r.arg1) > GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_GT_UINT </summary>
		void OperGreaterThanUint()
		{
			PutValueAsBool(r.res, (uint)GetValueAsInt(r.arg1) > (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_GT_LONG </summary>
		void OperGreaterThanLong()
		{
			PutValueAsBool(r.res, GetValueAsLong(r.arg1) > GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_GT_ULONG </summary>
		void OperGreaterThanUlong()
		{
			PutValueAsBool(r.res, (ulong)GetValueAsLong(r.arg1) > (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_GT_FLOAT </summary>
		void OperGreaterThanFloat()
		{
			PutValueAsBool(r.res, GetValueAsFloat(r.arg1) > GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_GT_DOUBLE </summary>
		void OperGreaterThanDouble()
		{
			PutValueAsBool(r.res, GetValueAsDouble(r.arg1) > GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_GT_DECIMAL </summary>
		void OperGreaterThanDecimal()
		{
			PutValueAsBool(r.res, GetValueAsDecimal(r.arg1) > GetValueAsDecimal(r.arg2));
			n++;
		}

		/// <summary> OP_GT_STRING </summary>
		void OperGreaterThanString()
		{
			int z = String.Compare(GetValueAsString(r.arg1), GetValueAsString(r.arg2));
			PutValueAsBool(r.res, z > 0);
			n++;
		}

		// DETAILED GE OPERATORS *****************************************

		/// <summary> OP_GE_INT </summary>
		void OperGreaterThanOrEqualInt()
		{
			PutValueAsBool(r.res, GetValueAsInt(r.arg1) >= GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_GE_UINT </summary>
		void OperGreaterThanOrEqualUint()
		{
			PutValueAsBool(r.res, (uint)GetValueAsInt(r.arg1) >= (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_GE_LONG </summary>
		void OperGreaterThanOrEqualLong()
		{
			PutValueAsBool(r.res, GetValueAsLong(r.arg1) >= GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_GE_ULONG </summary>
		void OperGreaterThanOrEqualUlong()
		{
			PutValueAsBool(r.res, (ulong)GetValueAsLong(r.arg1) >= (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_GE_FLOAT </summary>
		void OperGreaterThanOrEqualFloat()
		{
			PutValueAsBool(r.res, GetValueAsFloat(r.arg1) >= GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_GE_DOUBLE </summary>
		void OperGreaterThanOrEqualDouble()
		{
			PutValueAsBool(r.res, GetValueAsDouble(r.arg1) >= GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_GE_DECIMAL </summary>
		void OperGreaterThanOrEqualDecimal()
		{
			PutValueAsBool(r.res, GetValueAsDecimal(r.arg1) >= GetValueAsDecimal(r.arg2));
			n++;
		}

		/// <summary> OP_GE_STRING </summary>
		void OperGreaterThanOrEqualString()
		{
			int z = String.Compare(GetValueAsString(r.arg1), GetValueAsString(r.arg2));
			PutValueAsBool(r.res, z >= 0);
			n++;
		}

		// DETAILED EQ OPERATORS *****************************************

		/// <summary> OP_EQ_INT </summary>
		void OperEqualityInt()
		{
			PutValueAsBool(r.res, GetValueAsInt(r.arg1) == GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_UINT </summary>
		void OperEqualityUint()
		{
			PutValueAsBool(r.res, (uint)GetValueAsInt(r.arg1) == (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_LONG </summary>
		void OperEqualityLong()
		{
			PutValueAsBool(r.res, GetValueAsLong(r.arg1) == GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_ULONG </summary>
		void OperEqualityUlong()
		{
			PutValueAsBool(r.res, (ulong)GetValueAsLong(r.arg1) == (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_FLOAT </summary>
		void OperEqualityFloat()
		{
			PutValueAsBool(r.res, GetValueAsFloat(r.arg1) == GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_DOUBLE </summary>
		void OperEqualityDouble()
		{
			PutValueAsBool(r.res, GetValueAsDouble(r.arg1) == GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_DECIMAL </summary>
		void OperEqualityDecimal()
		{
			PutValueAsBool(r.res, GetValueAsDecimal(r.arg1) == GetValueAsDecimal(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_STRING </summary>
		void OperEqualityString()
		{
			PutValueAsBool(r.res, GetValueAsString(r.arg1) == GetValueAsString(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_BOOL </summary>
		void OperEqualityBool()
		{
			PutValueAsBool(r.res, GetValueAsBool(r.arg1) == GetValueAsBool(r.arg2));
			n++;
		}

		/// <summary> OP_EQ_OBJECT </summary>
		void OperEqualityObject()
		{
			PutValueAsBool(r.res, GetValue(r.arg1) == GetValue(r.arg2));
			n++;
		}

		// DETAILED NE OPERATORS *****************************************

		/// <summary> OP_NE_INT </summary>
		void OperInequalityInt()
		{
			PutValueAsBool(r.res, GetValueAsInt(r.arg1) != GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_NE_UINT </summary>
		void OperInequalityUint()
		{
			PutValueAsBool(r.res, (uint)GetValueAsInt(r.arg1) != (uint)GetValueAsInt(r.arg2));
			n++;
		}

		/// <summary> OP_NE_LONG </summary>
		void OperInequalityLong()
		{
			PutValueAsBool(r.res, GetValueAsLong(r.arg1) != GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_NE_ULONG </summary>
		void OperInequalityUlong()
		{
			PutValueAsBool(r.res, (ulong)GetValueAsLong(r.arg1) != (ulong)GetValueAsLong(r.arg2));
			n++;
		}

		/// <summary> OP_NE_FLOAT </summary>
		void OperInequalityFloat()
		{
			PutValueAsBool(r.res, GetValueAsFloat(r.arg1) != GetValueAsFloat(r.arg2));
			n++;
		}

		/// <summary> OP_NE_DOUBLE </summary>
		void OperInequalityDouble()
		{
			PutValueAsBool(r.res, GetValueAsDouble(r.arg1) != GetValueAsDouble(r.arg2));
			n++;
		}

		/// <summary> OP_NE_DECIMAL </summary>
		void OperInequalityDecimal()
		{
			PutValueAsBool(r.res, GetValueAsDecimal(r.arg1) != GetValueAsDecimal(r.arg2));
			n++;
		}

		/// <summary> OP_NE_STRING </summary>
		void OperInequalityString()
		{
			PutValueAsBool(r.res, GetValueAsString(r.arg1) != GetValueAsString(r.arg2));
			n++;
		}

		/// <summary> OP_NE_BOOL </summary>
		void OperInequalityBool()
		{
			PutValueAsBool(r.res, GetValueAsBool(r.arg1) != GetValueAsBool(r.arg2));
			n++;
		}

		/// <summary> OP_NE_OBJECT </summary>
		void OperInequalityObject()
		{
			PutValueAsBool(r.res, GetValue(r.arg1) != GetValue(r.arg2));
			n++;
		}

		/// <summary> Saves to stream </summary>
		public void SaveToStream(BinaryWriter bw, Module m)
		{
			int i1 = m.P1;
			int i2 = m.P2;

			for (int i = i1; i <= i2; i++)
				this[i].SaveToStream(bw);
		}

		/// <summary> Loads from stream </summary>
		public void LoadFromStream(BinaryReader br, Module m, int ds, int dp)
		{
			bool shift = (ds != 0) || (dp != 0);

			for (int i = m.P1; i <= m.P2; i++)
			{
				Card ++;
				r = this[Card];
				r.LoadFromStream(br);

				if (r.op == OP_SEPARATOR)
					continue;

				if (!shift)
					continue;

				if (m.IsInternalId(r.arg1))
					r.arg1 += ds;
				if (m.IsInternalId(r.arg2))
					r.arg2 += ds;
				if (m.IsInternalId(r.res))
					r.res += ds;
			}
		}

		/// <summary>
		/// Returns 'true' if op is a goto operator.
		/// </summary>
		bool IsGotoOper(int op)
		{
			return (op == OP_GO) || (op == OP_GO_FALSE) ||
				   (op == OP_GO_TRUE) || (op == OP_GO_NULL) ||
				   (op == OP_GOTO_START) || (op == OP_GOTO_CONTINUE);
		}

		bool GetUpcase()
		{
			return GetUpcase(n);
		}

		bool GetUpcase(int i)
		{
			if (i <= 1)
				return true;

			if (this[i].upcase == Upcase.Yes)
				return true;
			else if (this[i].upcase == Upcase.No)
				return false;
			else
				return GetUpcase(i - 1);
		}

		bool GetExplicit(int i)
		{
			for (int j = i; j >= 1; j--)
				if (this[j].op == OP_EXPLICIT_ON)
				   return true;
				else if (this[j].op == OP_EXPLICIT_OFF)
				   return false;
			return false;
		}

		bool GetStrict(int i)
		{
			for (int j = i; j >= 1; j--)
				if (this[j].op == OP_STRICT_ON)
				   return true;
				else if (this[j].op == OP_STRICT_OFF)
				   return false;
			return true;
		}

		int GetCurrMethodId()
		{
			int i = n;
			for(;;)
			{
				if (this[i].op == OP_INIT_METHOD)
					return this[i].arg1;
				i--;
				if (i <= 0)
					break;
			}
			return 0;
		}

        bool PascalOrBasic(int j)
        {
            PaxLanguage l = GetLanguage(j);
            return l == PaxLanguage.VB || l == PaxLanguage.Pascal;
        }

        internal void CollectResults(int sub_id, ResultList result_list)
        {
            result_list.Clear();
            for (int i = 1; i < Card; i++)
            {
                if ((this[i].op == OP_DECLARE_LOCAL_VARIABLE || this[i].op == OP_DECLARE_LOCAL_VARIABLE_RUNTIME) &&
                    this[i].arg2 == sub_id)
                {
                    int id = this[i].arg1;
                    string s = symbol_table[id].Name;
                    if (s.Length > 0)
                    {
                        char c = s[0];
                        if (BaseScanner.IsAlpha(c))
                        {
                            ScriptResult r = new ScriptResult(scripter, id);
                            result_list.Add(r);
                        }
                    }
                }
            }
        }

		/// <summary>
		/// Undocumented.
		/// </summary>
		public string Dump(string FileName)
		{
		#if dump
			StreamWriter t = File.CreateText(FileName);
            string result;
            StringList l = new StringList(true);

			int i;
			ProgRec r;
			for (i = 1; i <= Card; i++)
			{
				r = (ProgRec) prog[i];

				string index = PaxSystem.Norm(i, 5);
				string str;

				string op;
				if (r.op == OP_SEPARATOR)
				{
					Module m = GetModule(i);
					string module_name = m.Name;
					int line_number = GetLineNumber(i);
					string line = m.GetLine(line_number);

					str = index + " Module:" + module_name +
								  " Line:" + line_number.ToString() +
								  " " + line;
				}
				else
				{
					const int w = 30;

					op = GetOperName(r.op, 20);
					SymbolRec s1;
					if (IsGotoOper(r.op))
						s1 = symbol_table[r.arg2];
					else
						s1 = symbol_table[r.arg1];
					SymbolRec s2 = symbol_table[r.arg2];
					SymbolRec sr = symbol_table[r.res];

					string arg1 = PaxSystem.Norm(s1.Name, w - 7);
					arg1 += "[" + PaxSystem.Norm(r.arg1.ToString(), 5) + "]";
					string arg2 = PaxSystem.Norm(s2.Name, w - 7);
					arg2 += "[" + PaxSystem.Norm(r.arg2.ToString(), 5) + "]";
					string res = PaxSystem.Norm(sr.Name, w - 7);
					res += "[" + PaxSystem.Norm(r.res.ToString(), 5) + "]";

					if (r.arg1 == symbol_table.BR_id)
						arg1 = "";

					if ((r.op == OP_BEGIN_MODULE) || (r.op == OP_END_MODULE))
					{
						Module m = scripter.module_list.GetModule(r.arg1);
						arg1 = PaxSystem.Norm(m.Name, w);
						arg2 = PaxSystem.Norm(m.Language.ToString(), w);
					}

					if (r.op == OP_ADD_MODIFIER)
					   arg2 = PaxSystem.Norm(((Modifier) r.arg2).ToString(), arg2.Length);

					if ((r.op == OP_GO) ||
						(r.op == OP_GOTO_START) ||
						(r.op == OP_GO_FALSE) ||
						(r.op == OP_GO_NULL) ||
						(r.op == OP_GO_TRUE))
						arg1 = PaxSystem.Norm(r.arg1.ToString(), w);

					if ((r.op == OP_GOTO_CONTINUE) ||
						(r.op == OP_FINALLY) ||
						(r.op == OP_DISCARD_ERROR) ||
						(r.op == OP_EXIT_ON_ERROR) ||
						(r.op == OP_NOP) ||
						(r.op == OP_EVAL))
						arg1 = PaxSystem.Norm("", w);

					if ((r.op == OP_GO) ||
						(r.op == OP_GOTO_START) ||
						(r.op == OP_GOTO_CONTINUE) ||
						(r.op == OP_FINALLY) ||
						(r.op == OP_DISCARD_ERROR) ||
						(r.op == OP_EXIT_ON_ERROR) ||
						(r.op == OP_PRINT) ||
						(r.op == OP_TRY_ON) ||
						(r.op == OP_TRY_OFF) ||
						(r.op == OP_NOP) ||
						(r.op == OP_BEGIN_CALL) ||
						(r.op == OP_EVAL) ||
						(r.op == OP_CREATE_REFERENCE) ||
						(r.op == OP_BEGIN_USING) ||
						(r.op == OP_END_USING) ||
						(r.op == OP_SETUP_INDEX_OBJECT) ||
						(r.op == OP_HALT))
						arg2 = PaxSystem.Norm("", w);

					if ((r.op == OP_GO) ||
						(r.op == OP_GOTO_START) ||
						(r.op == OP_GOTO_CONTINUE) ||
						(r.op == OP_GO_FALSE) ||
						(r.op == OP_GO_NULL) ||
						(r.op == OP_GO_TRUE) ||
						(r.op == OP_FINALLY) ||
						(r.op == OP_DISCARD_ERROR) ||
						(r.op == OP_EXIT_ON_ERROR) ||
						(r.op == OP_TRY_ON) ||
						(r.op == OP_TRY_OFF) ||
						(r.op == OP_PRINT) ||
						(r.op == OP_NOP) ||
						(r.op == OP_BEGIN_CALL) ||
						(r.op == OP_CREATE_NAMESPACE) ||
						(r.op == OP_CREATE_CLASS) ||
						(r.op == OP_CREATE_FIELD) ||
						(r.op == OP_ADD_MODIFIER) ||
						(r.op == OP_BEGIN_USING) ||
						(r.op == OP_END_USING) ||
						(r.op == OP_ASSIGN_TYPE) ||
						(r.op == OP_CREATE_USING_ALIAS) ||
						(r.op == OP_ADD_INDEX) ||
						(r.op == OP_SETUP_INDEX_OBJECT) ||
						(r.op == OP_HALT))
						res = PaxSystem.Norm("", w);

					if ((r.op == OP_CALL) && (r.res == 0))
						res = PaxSystem.Norm("", w);

					str = index + " " + op + "          " +
						arg1 + " " + arg2 + " " + res;
				}

				if (r.op == OP_CALL)
				{
					int sub_id = r.arg1;
					object v = GetVal(sub_id);
					if (v != null && v is FunctionObject)
					{
						if ((v as FunctionObject).Init == null)
							str += "  init=null";
						else
							str += "  init=" + (v as FunctionObject).Init.FinalNumber;
					}
				}

                l.Add(str);

				t.WriteLine(str);
			}
            result = l.text;
			t.Close();
            return result;
        #else
            return "";
		#endif
		}
	}
	#endregion Code Class
}
