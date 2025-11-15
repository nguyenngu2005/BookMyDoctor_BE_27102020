using System;
using System.Text.Json;

namespace BookMyDoctor_WebAPI.RequestModel.Chat
{
    public static class LlmJsonParser
    {
        public static LlmStructuredOutput Parse(string json)
        {
            var result = new LlmStructuredOutput
            {
                Raw = json
            };

            if (string.IsNullOrWhiteSpace(json))
                return result;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return result;

            // helper lấy string
            static string GetString(JsonElement obj, string name)
                => obj.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
                    ? v.ToString()
                    : string.Empty;

            // helper bool
            static bool GetBool(JsonElement obj, string name)
            {
                if (!obj.TryGetProperty(name, out var v)) return false;

                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;

                if (v.ValueKind == JsonValueKind.String &&
                    bool.TryParse(v.ToString(), out var b))
                    return b;

                return false;
            }

            // helper int
            static int GetInt(JsonElement obj, string name)
                => obj.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

            // ====== map field chuẩn ======
            var intentStr = GetString(root, "intent");
            if (!Enum.TryParse<Intent>(intentStr, true, out var intent))
                intent = Intent.Unknown;
            result.Intent = intent;

            result.Symptom = GetString(root, "symptom");
            result.DoctorName = GetString(root, "doctorName");
            result.Department = GetString(root, "department");
            result.Date = GetString(root, "date");
            result.Time = GetString(root, "time");
            result.BookingId = GetInt(root, "bookingId");

            result.FullName = GetString(root, "fullName");
            result.Email = GetString(root, "email");
            result.Phone = GetString(root, "phone");

            result.NeedSymptom = GetBool(root, "needSymptom");
            result.NeedDate = GetBool(root, "needDate");
            result.NeedTime = GetBool(root, "needTime");
            result.NeedDoctor = GetBool(root, "needDoctor");

            result.NaturalReply = GetString(root, "naturalReply");

            // ====== đổ mọi property vào Extra (để phòng sau này thêm field mới) ======
            foreach (var prop in root.EnumerateObject())
            {
                if (result.Extra.ContainsKey(prop.Name))
                    continue;

                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    result.Extra[prop.Name] = prop.Value.ToString();
                else
                    result.Extra[prop.Name] = prop.Value.ToString();
            }

            return result;
        }
    }
}
