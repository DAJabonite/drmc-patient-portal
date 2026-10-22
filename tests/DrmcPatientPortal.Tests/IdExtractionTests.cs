using DrmcPatientPortal.Models;
using DrmcPatientPortal.Services;

namespace DrmcPatientPortal.Tests;

public class IdExtractionTests
{
    public static TheoryData<string, string> SupportedDocuments => new()
    {
        { PhilippineIdTypes.PhilSys, "1234-5678-9012-3456" },
        { PhilippineIdTypes.DriversLicense, "D01-23-456789" },
        { PhilippineIdTypes.Passport, "P1234567" },
        { PhilippineIdTypes.Umid, "1234-1234567-1" },
        { PhilippineIdTypes.PostalId, "POST-123456" },
        { PhilippineIdTypes.PhilHealth, "12-123456789-1" },
        { PhilippineIdTypes.SssGsis, "12-1234567-1" },
        { PhilippineIdTypes.PrcId, "1234567" },
        { PhilippineIdTypes.SeniorCitizen, "SC-000123" },
        { PhilippineIdTypes.Pwd, "PWD-000456" },
        { PhilippineIdTypes.OtherGovernment, "EMP-000789" }
    };

    [Theory]
    [MemberData(nameof(SupportedDocuments))]
    public void All_supported_documents_preserve_bilingual_names_and_extract_their_number(string type, string number)
    {
        var numberLabel = type switch
        {
            PhilippineIdTypes.Passport => "Passport No.",
            PhilippineIdTypes.DriversLicense => "License No.",
            PhilippineIdTypes.Umid => "CRN",
            PhilippineIdTypes.PhilHealth => "PIN",
            PhilippineIdTypes.SssGsis => "SSS No.",
            PhilippineIdTypes.PrcId => "Registration No.",
            _ => "ID Number"
        };
        var result = Parse(type, $"""
            {numberLabel}: {number}
            Apelyido / Last Name
            SANTOS
            Mga Pangalan / Given Names
            MARIA CLARA
            Gitnang Apelyido / Middle Name
            REYES
            Petsa ng Kapanganakan / Date of Birth
            MARCH 08, 1995
            Tirahan / Address
            12 SAMPLE STREET, BARANGAY UNO
            SAMPLE CITY, 8100
            """, "Kasarian / Sex\nFEMALE\nUri ng dugo / Blood type\nUNKNOWN");

        Assert.True(result.Success);
        Assert.Equal("Maria Clara", result.FirstName.Value);
        Assert.Equal("Reyes", result.MiddleName.Value);
        Assert.Equal("Santos", result.LastName.Value);
        Assert.Equal(number, result.IdNumber.Value);
        Assert.False(result.BloodType.Found);
    }

    [Fact]
    public void Adjacent_name_column_headers_are_associated_with_their_values()
    {
        var result = Parse(PhilippineIdTypes.DriversLicense,
            "Last Name, First Name, Middle Name\nSANTOS, MARIA CLARA, REYES\nLicense No. D01-23-456789");
        Assert.Equal("Santos", result.LastName.Value);
        Assert.Equal("Maria Clara", result.FirstName.Value);
        Assert.Equal("Reyes", result.MiddleName.Value);
    }

    [Fact]
    public void Address_stops_at_uncollected_fields_and_birthplace_is_not_a_birth_date()
    {
        var result = Parse(PhilippineIdTypes.OtherGovernment, """
            Address: 12 SAMPLE STREET
            SAMPLE CITY 8100
            Nationality: FILIPINO
            Place of Birth: OTHER CITY
            Marital Status: SINGLE
            Date of Issue: 01/02/2020
            Expiration Date: 01/02/2030
            """);
        Assert.Equal("12 SAMPLE STREET SAMPLE CITY 8100", result.Address.Value);
        Assert.False(result.DateOfBirth.Found);
        Assert.False(result.FirstName.Found);
    }

    [Theory]
    [InlineData("MARCH 08, 1995", true)]
    [InlineData("1995-03-08", true)]
    [InlineData("08 MAR 1995", true)]
    [InlineData("3/8/1995", true)]
    [InlineData("02/30/1995", false)]
    [InlineData("MARCH 08, 19995", false)]
    [InlineData("MARCH 08, 2095", false)]
    [InlineData("UNKNOWN", false)]
    public void Dates_require_a_complete_valid_nonfuture_date(string text, bool expected)
    {
        var result = Parse(PhilippineIdTypes.OtherGovernment, "Date of Birth: " + text);
        Assert.Equal(expected, result.DateOfBirth.Found);
    }

    [Fact]
    public void Generic_number_requires_a_label_and_cannot_be_an_expiration_date()
    {
        var result = Parse(PhilippineIdTypes.SeniorCitizen, "12345678\nExpiration Date: 2030-03-08");
        Assert.False(result.IdNumber.Found);
        Assert.False(result.DateOfBirth.Found);
    }

    [Fact]
    public void Common_label_misreads_are_recognized_without_changing_values()
    {
        var result = Parse(PhilippineIdTypes.PhilSys, """
            Last Name: SANTOS
            Given Names: MARIA CLARA
            Gitnang Apelyido / Middie Name: REYES
            Date of Burth: MARCH 08, 1995
            Tratan / Addess: 12 SAMPLE STREET
            """, "Kasarion / Sex: FEMALE");
        Assert.Equal("Reyes", result.MiddleName.Value);
        Assert.Equal("Santos", result.LastName.Value);
        Assert.Equal(new DateTime(1995, 3, 8), result.DateOfBirth.Value);
        Assert.Equal("12 SAMPLE STREET", result.Address.Value);
        Assert.Equal("Female", result.Sex.Value);
    }

    [Fact]
    public void Compound_family_names_and_printed_middle_initials_are_preserved()
    {
        var result = Parse(PhilippineIdTypes.PhilHealth,
            "Last Name: DE LA CRUZ\nGiven Names: MARIA CLARA\nMiddle Name: D.\nPIN: 12-123456789-1");
        Assert.Equal("De La Cruz", result.LastName.Value);
        Assert.Equal("Maria Clara", result.FirstName.Value);
        Assert.Equal("D.", result.MiddleName.Value);
    }

    [Fact]
    public void Empty_or_unlabelled_noise_does_not_invent_personal_details()
    {
        var result = Parse(PhilippineIdTypes.PhilSys, "REPUBLIC OF THE PHILIPPINES\nPHL\nnoise", "UNKNOWN\nSINGLE");
        Assert.False(result.Success);
        Assert.Empty(result.FoundFieldsSummary);
        Assert.Empty(Parse(PhilippineIdTypes.Pwd, "Name: L, -").FoundFieldsSummary);
    }

    [Fact]
    public void Passport_mrz_keeps_all_given_names_and_checks_number_and_birth_date_digits()
    {
        var first = "P<PHLSANTOS<<MARIA<CLARA".PadRight(44, '<');
        var number = "P1234567<";
        var dob = "950308";
        var second = (number + Check(number) + "PHL" + dob + Check(dob) + "F3003080").PadRight(44, '<');
        var result = Parse(PhilippineIdTypes.Passport, first + "\n" + second);
        Assert.Equal("Maria Clara", result.FirstName.Value);
        Assert.Equal("Santos", result.LastName.Value);
        Assert.False(result.MiddleName.Found);
        Assert.Equal("P1234567", result.IdNumber.Value);
        Assert.Equal(new DateTime(1995, 3, 8), result.DateOfBirth.Value.Date);

        var damaged = second.ToCharArray();
        damaged[9] = (char)('0' + ((damaged[9] - '0' + 1) % 10));
        damaged[19] = (char)('0' + ((damaged[19] - '0' + 1) % 10));
        var rejected = Parse(PhilippineIdTypes.Passport, first + "\n" + new string(damaged));
        Assert.False(rejected.IdNumber.Found);
        Assert.False(rejected.DateOfBirth.Found);

        static char Check(string text)
        {
            int[] weights = [7, 3, 1];
            return (char)('0' + text.Select((c, i) => (char.IsDigit(c) ? c - '0' : c == '<' ? 0 : c - 'A' + 10) * weights[i % 3]).Sum() % 10);
        }
    }

    [Theory]
    [InlineData("P<PHL")]
    [InlineData("P<PHL<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<\n123")]
    [InlineData("P<PHL<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<\nXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX")]
    public void Malformed_mrz_does_not_invent_a_number_or_date(string text)
    {
        var result = Parse(PhilippineIdTypes.Passport, text);
        Assert.False(result.IdNumber.Found);
        Assert.False(result.DateOfBirth.Found);
    }

    [Fact]
    public void Candidate_merging_retains_reliable_fields_and_fills_missing_fields()
    {
        var current = new IdExtractionResult();
        IdExtractionCandidates.Merge(current, Parse(PhilippineIdTypes.Pwd, "First Name: MARIA CLARA\nLast Name: SANTOS"), .95f);
        IdExtractionCandidates.Merge(current, Parse(PhilippineIdTypes.Pwd, "First Name: OTHER\nID Number: PWD-000456"), .6f);
        Assert.Equal("Maria Clara", current.FirstName.Value);
        Assert.Equal("Santos", current.LastName.Value);
        Assert.Equal("PWD-000456", current.IdNumber.Value);
        IdExtractionCandidates.Merge(current, new(), .99f);
        Assert.Equal("PWD-000456", current.IdNumber.Value);
    }

    [Fact]
    public void Postcode_agreement_counts_independent_scales_instead_of_repeated_passes()
    {
        var wrong = ExtractedField<string>.From("12 SAMPLE STREET SAMPLE CITY 8160", .96f);
        var right = ExtractedField<string>.From("12 SAMPLE STREET SAMPLE CITY 8100", .85f);
        var selected = IdImageRecognition.ResolveAddress(wrong,
            [(wrong, 1500), (wrong, 1500), (wrong, 1500), (right, 1000), (right, 2000)]);
        Assert.Equal(right.Value, selected.Value);
    }

    internal static IdExtractionResult Parse(string type, string front, string back = "")
    {
        var result = new IdExtractionResult { IdType = type };
        TesseractIdDocumentExtractionService.ParseStructuredFields(result, type, front, back);
        return result;
    }
}
