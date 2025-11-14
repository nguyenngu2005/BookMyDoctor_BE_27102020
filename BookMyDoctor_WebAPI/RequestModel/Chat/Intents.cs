namespace BookMyDoctor_WebAPI.RequestModel.Chat
{
    public enum Intent
    {
        Unknown,

        // Bot chào hỏi, hướng dẫn
        GreetingHelp,

        // Tìm bác sĩ theo tên/khoa
        SearchDoctors,

        // Kiểm tra giờ trống trong 1 ngày
        GetBusySlots,

        // Đặt lịch khám
        CreatePublicBooking,

        // Hủy lịch
        CancelBooking,

        // Hỏi đáp FAQ
        Faq,

        // ✅ Ý định mở rộng mới
        CountDoctors,                // "Có bao nhiêu bác sĩ?"
        DoctorScheduleRange,         // "Lịch bác sĩ A trong 7 ngày tới"
        CountDepartments,            // "Có bao nhiêu khoa?"
        ListDepartments,             // "Có những khoa nào?"
        DoctorInfo,                  // "Thông tin bác sĩ A?"
        ClinicInfo,                  // "Phòng khám làm việc giờ nào? Địa chỉ?"
        PriceInquiry,                // "Khám tổng quát giá bao nhiêu?"
        InsuranceInquiry,            // "Có nhận bảo hiểm y tế không?"
        EmergencyGuide,              // "Trường hợp khẩn cấp thì làm sao?"
    }
}
