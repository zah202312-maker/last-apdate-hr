using System;
using System.Collections.Generic;

namespace HR_Mangment2.DemoData;

/// <summary>The made-up company: three departments, a manager and four staff each, and the back-office roles.</summary>
public static class DemoCatalog
{
    public record PersonSpec(string Key, string NameAr, string NameEn, bool Female, string JobTitle, DateOnly HireDate, decimal BaseSalary);

    public record DepartmentSpec(string Key, string NameAr, string NameEn, PersonSpec Manager, IReadOnlyList<PersonSpec> Staff);

    public static readonly IReadOnlyList<DepartmentSpec> Departments = new[]
    {
        new DepartmentSpec("sales", "المبيعات", "Sales",
            new PersonSpec("ali", "علي حسن كاظم", "Ali Hasan Kadhim", false, "مدير المبيعات", new DateOnly(2022, 3, 6), 2_400_000m),
            new[]
            {
                new PersonSpec("haider", "حيدر سعد عباس", "Haider Saad Abbas", false, "مندوب مبيعات", new DateOnly(2023, 1, 15), 1_100_000m),
                new PersonSpec("maryam", "مريم علي جاسم", "Maryam Ali Jasim", true, "مندوبة مبيعات", new DateOnly(2023, 9, 3), 1_050_000m),
                new PersonSpec("karrar", "كرار حسين مهدي", "Karrar Hussein Mahdi", false, "محاسب مبيعات", new DateOnly(2024, 2, 11), 1_250_000m),
                new PersonSpec("zahraa", "زهراء كريم ناصر", "Zahraa Kareem Nasser", true, "منسقة علاقات زبائن", new DateOnly(2024, 10, 20), 950_000m)
            }),
        new DepartmentSpec("it", "تقنية المعلومات", "Information Technology",
            new PersonSpec("noor", "نور محمد جاسم", "Noor Mohammed Jasim", true, "مديرة تقنية المعلومات", new DateOnly(2021, 11, 1), 2_600_000m),
            new[]
            {
                new PersonSpec("ahmed", "أحمد فاضل عيسى", "Ahmed Fadhil Issa", false, "مطور برمجيات", new DateOnly(2022, 6, 12), 1_600_000m),
                new PersonSpec("sara", "سارة عماد طه", "Sara Emad Taha", true, "مهندسة شبكات", new DateOnly(2023, 4, 2), 1_450_000m),
                new PersonSpec("yousif", "يوسف خالد إبراهيم", "Yousif Khalid Ibrahim", false, "دعم فني", new DateOnly(2024, 1, 7), 900_000m),
                new PersonSpec("ruqaya", "رقية جواد كاظم", "Ruqaya Jawad Kadhim", true, "محللة نظم", new DateOnly(2025, 3, 16), 1_300_000m)
            }),
        new DepartmentSpec("ops", "العمليات", "Operations",
            new PersonSpec("mustafa", "مصطفى عبد الله ياسين", "Mustafa Abdullah Yaseen", false, "مدير العمليات", new DateOnly(2020, 9, 13), 2_500_000m),
            new[]
            {
                new PersonSpec("abbas", "عباس ناظم حمزة", "Abbas Nadhim Hamza", false, "مشرف ميداني", new DateOnly(2021, 5, 9), 1_350_000m),
                new PersonSpec("fatima", "فاطمة رعد محسن", "Fatima Raad Mohsen", true, "منسقة عمليات", new DateOnly(2022, 12, 4), 1_150_000m),
                new PersonSpec("mohammed", "محمد قاسم علوان", "Mohammed Qasim Alwan", false, "سائق", new DateOnly(2023, 7, 23), 800_000m),
                new PersonSpec("duaa", "دعاء صباح هادي", "Duaa Sabah Hadi", true, "موظفة استقبال", new DateOnly(2024, 8, 18), 850_000m)
            })
    };

    // Used only when the company has no holder of the role yet.
    public static readonly PersonSpec Accountant = new("accountant", "حسين جبار سلمان", "Hussein Jabbar Salman", false, "محاسب", new DateOnly(2021, 2, 14), 1_700_000m);
    public static readonly PersonSpec StoreOfficer = new("store", "سجاد رحيم عودة", "Sajjad Raheem Odeh", false, "مسؤول المخزن", new DateOnly(2022, 8, 28), 1_200_000m);
    public static readonly PersonSpec Reviewer = new("reviewer", "هدى سالم عبد الرحمن", "Huda Salem Abdulrahman", true, "مراجعة", new DateOnly(2021, 4, 4), 1_900_000m);
}
