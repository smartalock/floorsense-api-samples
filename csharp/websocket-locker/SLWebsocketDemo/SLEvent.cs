using System;
using System.Text.Json.Nodes;

namespace Smartalock.API
{
    public enum SLEventCode : int
    {
        EVENT_CODE_LOCK_STATUS	= 0x11,
        EVENT_CODE_OPEN			= 0x12,
        EVENT_CODE_CLOSE		= 0x13,
        EVENT_CODE_TAMPER		= 0x14,
        EVENT_CODE_OPEN_LONG	= 0x15,
        EVENT_CODE_LIGHT		= 0x16,
        EVENT_CODE_LED			= 0x17,
        EVENT_CODE_BUZZER		= 0x18,
        EVENT_CODE_USB_CHG		= 0x19,

        EVENT_CODE_DESK_STATUS	= 0x1A,
        EVENT_CODE_DESK_MOTION	= 0x1B,
        EVENT_CODE_DESK_OCCUPIED = 0x1C,
        EVENT_CODE_DESK_UNOCCUPIED = 0x1D,
        EVENT_CODE_DESK_RFID_READ = 0x1E,
        EVENT_CODE_DESK_BUTTON = 0x1F,

        EVENT_CODE_RESERVED		= 0x21,
        EVENT_CODE_RELEASED		= 0x22,
        EVENT_CODE_SHARED		= 0x23,
        EVENT_CODE_UNSHARED		= 0x24,
        EVENT_CODE_RES_UPDATE	= 0x25,
        EVENT_CODE_RES_ACTIVATE	= 0x26,
        EVENT_CODE_RES_CONFIRM	= 0x27,

        EVENT_CODE_BOOKING_CREATE = 0x31,
        EVENT_CODE_BOOKING_RELEASE = 0x32,
        EVENT_CODE_BOOKING_UPDATE = 0x33,
        EVENT_CODE_BOOKING_ACTIVATE = 0x34,
        EVENT_CODE_BOOKING_CONFIRM = 0x35,
        EVENT_CODE_BOOKING_CREATE_REPEAT = 0x36,
        EVENT_CODE_BOOKING_UPDATE_REPEAT = 0x37,
        EVENT_CODE_BOOKING_EXPIRE_REPEAT = 0x38,
        EVENT_CODE_BOOKING_CANCEL = 0x39,
        EVENT_CODE_CHECKIN_CREATE	= 0x3A,
        EVENT_CODE_CHECKIN_RELEASE	= 0x3B,
        EVENT_CODE_DESK_POWER_ON	= 0x3C,
        EVENT_CODE_DESK_POWER_OFF	= 0x3D,
        EVENT_CODE_WAITLIST_CREATE = 0x3E,
        EVENT_CODE_WAITLIST_UPDATE = 0x3F,

        EVENT_CODE_BANK_STATUS	= 0x41,

        EVENT_CODE_NET_STATUS	= 0x51,
        EVENT_CODE_DESK_CLEAN	= 0x57,
        EVENT_CODE_DESK_NOT_CLEAN	= 0x58,
        EVENT_CODE_DESK_HEIGHT	= 0x59,

        EVENT_CODE_RFID_READ	= 0x81,
    }

    public class SLEvent
    {
        public int Code { get; }
        public string? Message { get; }
        public JsonNode? Info { get; }

        public SLEvent(int code, string? message, JsonNode? info)
        {
            this.Code = code;
            this.Message = message;
            this.Info = info;
        }
    }
}
