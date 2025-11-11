namespace BookMyDoctor_WebAPI.RequestModel.Chat;

public enum Intent
{
    Unknown = 0,
    GreetingHelp,
    SearchDoctors,
    GetBusySlots,
    CreatePublicBooking,
    CancelBooking,
    Faq
}