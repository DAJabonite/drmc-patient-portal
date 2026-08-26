using DrmcPatientPortal.Data;
using DrmcPatientPortal.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrmcPatientPortal.Controllers;

public class DirectoryController : Controller
{
    private readonly ApplicationDbContext _db;

    public DirectoryController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /Directory
    public async Task<IActionResult> Index(string? department, string? search, bool? teleconsultOnly)
    {
        var query = _db.Doctors.Where(d => d.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(department))
        {
            query = query.Where(d => d.Department == department);
        }

        if (teleconsultOnly == true)
        {
            query = query.Where(d => d.OffersTeleconsult);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(d => d.FullName.ToLower().Contains(s) 
                                  || d.SubSpecialty.ToLower().Contains(s) 
                                  || d.Department.ToLower().Contains(s)
                                  || d.ClinicRoom.ToLower().Contains(s));
        }

        var doctors = await query.OrderBy(d => d.Department).ThenBy(d => d.FullName).ToListAsync();

        var model = new DirectoryIndexViewModel
        {
            SelectedDepartment = department,
            SearchTerm = search,
            TeleconsultOnly = teleconsultOnly ?? false,
            Doctors = doctors,
            Departments = ClinicalDepartments.All
        };

        return View(model);
    }

    // GET /Directory/Doctor/5
    public async Task<IActionResult> Doctor(int id)
    {
        var doctor = await _db.Doctors.FindAsync(id);
        if (doctor is null)
        {
            return NotFound();
        }

        var dept = ClinicalDepartments.All.FirstOrDefault(d => d.Name == doctor.Department);

        var model = new DoctorProfileViewModel
        {
            Doctor = doctor,
            DepartmentInfo = dept
        };

        return View(model);
    }

    // GET /Directory/Department/Internal%20Medicine or /Directory/Department?name=Internal%20Medicine
    [HttpGet]
    public async Task<IActionResult> Department(string? id, string? name)
    {
        var deptName = name ?? id;
        if (string.IsNullOrWhiteSpace(deptName))
        {
            return RedirectToAction(nameof(Index));
        }

        var dept = ClinicalDepartments.All.FirstOrDefault(d => string.Equals(d.Name, deptName, StringComparison.OrdinalIgnoreCase));
        if (dept is null)
        {
            return NotFound();
        }

        var doctors = await _db.Doctors
            .Where(d => d.Department == dept.Name && d.IsActive)
            .OrderBy(d => d.FullName)
            .ToListAsync();

        var model = new DepartmentDetailViewModel
        {
            Department = dept,
            Doctors = doctors
        };

        return View(model);
    }
}

public class DirectoryIndexViewModel
{
    public string? SelectedDepartment { get; set; }
    public string? SearchTerm { get; set; }
    public bool TeleconsultOnly { get; set; }
    public IReadOnlyList<Doctor> Doctors { get; set; } = Array.Empty<Doctor>();
    public IReadOnlyList<ClinicalDepartment> Departments { get; set; } = Array.Empty<ClinicalDepartment>();
}

public class DoctorProfileViewModel
{
    public Doctor Doctor { get; set; } = null!;
    public ClinicalDepartment? DepartmentInfo { get; set; }
}

public class DepartmentDetailViewModel
{
    public ClinicalDepartment Department { get; set; } = null!;
    public IReadOnlyList<Doctor> Doctors { get; set; } = Array.Empty<Doctor>();
}
