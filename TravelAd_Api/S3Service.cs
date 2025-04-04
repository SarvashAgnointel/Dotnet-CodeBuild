using Amazon.Runtime;
using Amazon.S3.Model;
using Amazon.S3;
using Microsoft.Data.SqlClient;
using System.Data;
using DBAccess;
using Amazon;
using CsvHelper;
using System.Globalization;
using OfficeOpenXml;
using System.Dynamic;
using Org.BouncyCastle.Crypto.Tls;
using OfficeOpenXml.FormulaParsing.Excel.Functions.RefAndLookup;

public class S3Service
{
    private readonly string _bucketName = "operator-contact-bucket"; // Replace with your actual bucket name.
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _configuration;
    private readonly IDbHandler _dbHandler;

    // Constructor to inject the DbHandler and IConfiguration dependencies
    public S3Service(IConfiguration configuration, IDbHandler dbHandler)
    {
        _configuration = configuration;
        _dbHandler = dbHandler;

        //for local testing
        var awsCredentials = new SessionAWSCredentials("ASIA4SDNVWWKU3JAUOB4", "Xn51RCKb0UzXl9p02CCCXxKRB7yrEErCPIrWx+2D", "IQoJb3JpZ2luX2VjEPf//////////wEaCXVzLWVhc3QtMSJGMEQCIH4Vo1Woxu52/c9sJSuHgLnYYA66K69oiE1BWNRpBgFXAiAOdOBsdoNXDLZcJWBLWYyRj+3E2lJaPG3bizuDuz93iirrAghgEAAaDDg2MzUxODQzODgwNSIMIntNXYoxmNGsp5xEKsgCjCrEsgyQtG7pAADV+0OM2+kytbGGV1u93UnG+yPwDLKV4iAlHxI2eD8hQnixZ5A4Aw/E0Qn0mIkBpDGpaNfAC9odhu3rqgOSlTKIwzf9OIetb7ZEIrskaC7YM3H1y+qdZKH77g47ZVXTm620B+EvTX2zWURnbSo96ttyQClt/zSfuC70xVXyPNadzSWjmHdfMxP4HseVrmKtauOf6CqVc5fbjUGx7CQObmrM8ltDv1c3RnSmAeCHhanq8IbOoRivPdRDZCHkRwCKGT/RT2g0L/f1FcrkPchIopuOg0rSmlCopuNDfrTPD7xus/AkZcKXm9yB71PXZzG0uYvCFodVy46KlJs9XrC2HjGc2hC13EI6oO4JBnh16/JnyIKl8u48BT174Uf/VqUVl8InSjbvvOypasv/lrOiYE+T6stjzfhWpYI2h+dNSDDE5pq/BjqoAVG915ucTf8XW0+Yqxo7xsjQEkDfLVHK65XH4xcRq51aFDkhx8BNcIrBkFcH8lKXGw+cdXm5xBiVD4j5b9c4JYGUlYXzrwV9G/IXK6C/Mhqmv7sZ0QncykNFh319AsdpMblqri5f5R4vFB+JR5lXk1lo9No2vRrqaJuty2dHhXM9DrYulPwKQQirKO4oScRlC1n1EU+PHCHfVnMP2O65SBuPsu/hGIDRGQ==");
        _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
        
        //for cloud deployment
        //_s3Client = new AmazonS3Client(RegionEndpoint.USEast1);// Correct initialization
    }

    private IDbHandler DbHandler => _dbHandler;
    public async Task<List<string>> GetAllLatestFileNamesAsync()
    {
        var latestFiles = new List<string>();

        var folders = _configuration.GetSection("S3Settings:Folders").Get<List<string>>();
        if (folders == null || !folders.Any())
        {
            Console.WriteLine("No folders configured in appsettings.json");
            return latestFiles;
        }

        foreach (var folder in folders)
        {
            string prefix = folder.EndsWith("/") ? folder : folder + "/";

            var listFilesRequest = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix
            };

            var fileResponse = await _s3Client.ListObjectsV2Async(listFilesRequest);

            var latestFile = fileResponse.S3Objects
                .Where(o => !o.Key.StartsWith("backup_contacts/") && !o.Key.EndsWith("/"))
                .OrderByDescending(o => o.LastModified)
                .FirstOrDefault();

            if (latestFile != null)
            {
                latestFiles.Add(latestFile.Key);
            }
        }

        return latestFiles;
    }




    public async Task<Stream?> GetFileStreamAsync(string fileName)
    {
        try
        {
            var response = await _s3Client.GetObjectAsync(_bucketName, fileName);
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving file '{fileName}': {ex.Message}");
            return null;
        }
    }


    public async Task MoveFileToBackupAsync(string folderName, string fileName)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        string originalKey = $"{folderName}/{fileName}"; // Full key
        string originalFileName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        string backupKey = $"backup_contacts/{folderName}/{originalFileName}_{timestamp}{extension}";

        try
        {
            // Copy original file to backup location
            await _s3Client.CopyObjectAsync(_bucketName, originalKey, _bucketName, backupKey);

            // Delete original file
            await _s3Client.DeleteObjectAsync(_bucketName, originalKey);

            Console.WriteLine($"Moved '{originalKey}' to '{backupKey}'");
        }
        catch (AmazonS3Exception ex)
        {
            Console.WriteLine($"S3 Error: {ex.Message} (Key: {originalKey})");
            throw;
        }
    }





}





public class FileDownloadAndBackupService : BackgroundService
{
    private readonly ILogger<FileDownloadAndBackupService> _logger;
    private readonly S3Service _s3Service;
    private readonly IConfiguration _configuration;  // Injected IConfiguration
    private readonly IDbHandler _dbHandler;  // Injected IDbHandler
    private readonly string _localFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "S3 Files");


    public FileDownloadAndBackupService(
    ILogger<FileDownloadAndBackupService> logger,
    S3Service s3Service,
    IConfiguration configuration,
    IDbHandler dbHandler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _s3Service = s3Service ?? throw new ArgumentNullException(nameof(s3Service));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _dbHandler = dbHandler ?? throw new ArgumentNullException(nameof(dbHandler));
    }


    private bool ValidateRow(dynamic row, out string errorMessage)
    {
        errorMessage = string.Empty;

        // Safely cast dynamic row to dictionary
        var dictRow = row as IDictionary<string, object>;
        if (dictRow == null)
        {
            errorMessage = "Row is not a valid dictionary.";
            return false;
        }

        // Build a case-insensitive key map
        var keyMap = dictRow.Keys.ToDictionary(k => k.ToLower(), k => k);

        // Helper function to get a value safely
        string GetSafeValue(string columnName) =>
            keyMap.ContainsKey(columnName.ToLower()) ? dictRow[keyMap[columnName.ToLower()]]?.ToString() : null;

        // Define required and optional columns
        var requiredColumns = new[] { "countryName", "msisdn" };
        var optionalColumns = new[] { "mcc", "mnc","eventTimeStamp" };

        // Check for missing required columns
        var missingRequired = requiredColumns.Where(c => !keyMap.ContainsKey(c.ToLower())).ToList();
        if (missingRequired.Any())
        {
            errorMessage = $"Missing required columns: {string.Join(", ", missingRequired)}";
            return false;
        }

        // Log any missing optional columns
        var missingOptional = optionalColumns.Where(c => !keyMap.ContainsKey(c.ToLower())).ToList();
        if (missingOptional.Any())
        {
            _logger.LogWarning("Optional columns missing in file: {MissingColumns}", string.Join(", ", missingOptional));
        }

        // Validate eventTimeStamp
        if (!DateTime.TryParse(GetSafeValue("eventTimeStamp"), out _))
        {
            errorMessage = $"Invalid eventTimeStamp: '{GetSafeValue("eventTimeStamp")}' is not a valid date.";
            return false;
        }

        // Validate msisdn
        string msisdn = GetSafeValue("msisdn") ?? "";
        if (string.IsNullOrWhiteSpace(msisdn))
        {
            errorMessage = "Invalid msisdn: Value is missing or empty.";
            return false;
        }

        if (msisdn.Length != 10 || !msisdn.All(char.IsDigit))
        {
            errorMessage = $"Invalid msisdn: '{msisdn}' must be 10 digits.";
            return false;
        }

        // Optional: Validate mcc if present
        if (keyMap.ContainsKey("mcc"))
        {
            if (!int.TryParse(GetSafeValue("mcc"), out _))
            {
                errorMessage = $"Invalid mcc: '{GetSafeValue("mcc")}' is not a valid integer.";
                return false;
            }
        }

        // Optional: Validate mnc if present
        if (keyMap.ContainsKey("mnc"))
        {
            if (!int.TryParse(GetSafeValue("mnc"), out _))
            {
                errorMessage = $"Invalid mnc: '{GetSafeValue("mnc")}' is not a valid integer.";
                return false;
            }
        }

        // Optional: Validate countryName if present
        if (keyMap.ContainsKey("countryname"))
        {
            string countryName = GetSafeValue("countryName")?.Trim() ?? "";
            if (!countryName.All(c => char.IsLetter(c) || char.IsWhiteSpace(c)))
            {
                errorMessage = $"Invalid countryName: '{countryName}' contains invalid characters.";
                return false;
            }
        }

        return true;
    }




    private async Task InsertFailureAsync(DataRow failedRow, string errorMessage, int workspaceId)
    {
        string procedure = "InsertFailureOperatorContacts"; // Define the stored procedure name
        var parameters = new Dictionary<string, object>
    {
        { "@eventTimeStamp", failedRow["eventTimeStamp"] },
        { "@msisdn", failedRow["msisdn"] },
        { "@mcc", failedRow["mcc"] },
        { "@mnc", failedRow["mnc"] },
        { "@countryName", failedRow["countryName"] },
        { "@errorMessage", errorMessage },
        { "@fileName", failedRow["fileName"] },
        { "@workspaceId", workspaceId }  // Add workspaceId
    };

        await _dbHandler.ExecuteNonQueryAsync(procedure, parameters, CommandType.StoredProcedure);
    }


    private List<dynamic> ReadCsvTxtStream(Stream stream)
    {
        var records = new List<dynamic>();
        stream.Position = 0;

        using var reader = new StreamReader(stream);

        // Read the first line to detect the delimiter
        string? firstLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(firstLine)) return records;

        // Detect delimiter
        string delimiter = firstLine.Contains("|") ? "|" : ",";

        // Rewind the stream and re-read with proper CSV parser
        stream.Position = 0;
        using var finalReader = new StreamReader(stream);
        using var csv = new CsvReader(finalReader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            IgnoreBlankLines = true,
            HeaderValidated = null,
            MissingFieldFound = null
        });

        records = csv.GetRecords<dynamic>().ToList();
        return records;
    }



    // Helper method to read Excel files
    private List<dynamic> ReadExcelStream(Stream stream)
    {
        var records = new List<dynamic>();
        using var package = new ExcelPackage(stream);
        var worksheet = package.Workbook.Worksheets[0];

        for (int row = worksheet.Dimension.Start.Row + 1; row <= worksheet.Dimension.End.Row; row++)
        {
            var rowObj = new ExpandoObject() as IDictionary<string, object>;
            for (int col = worksheet.Dimension.Start.Column; col <= worksheet.Dimension.End.Column; col++)
            {
                rowObj.Add(worksheet.Cells[1, col].Text, worksheet.Cells[row, col].Text);
            }
            records.Add(rowObj);
        }

        return records;
    }





    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File download and backup service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var latestFileNames = await _s3Service.GetAllLatestFileNamesAsync();


                if (latestFileNames == null || !latestFileNames.Any())
                {
                    _logger.LogInformation("No files found in any operator folder.");
                    Console.WriteLine("No files found in any operator folder");
                    continue;
                }

                foreach (var latestFileName in latestFileNames)
                {
                    // latestFileName = "JIO TECH/JIO TECH_contacts.csv"
                    string folderName = Path.GetDirectoryName(latestFileName)?.Replace("\\", "/") ?? "";
                    string fileNameOnly = Path.GetFileName(latestFileName); // "JIO TECH_contacts.csv"

                    _logger.LogInformation("Folder: {FolderName}, File: {FileName}", folderName, fileNameOnly);




                    if (!string.IsNullOrEmpty(latestFileName))
                    {
                        _logger.LogInformation("Latest file found: {FileName}", latestFileName);

                        var stream = await _s3Service.GetFileStreamAsync(latestFileName);

                        if (stream == null) continue;


                        if (stream != null)
                        {

                            await _s3Service.MoveFileToBackupAsync(folderName, fileNameOnly);
                            _logger.LogInformation("File {FileName} moved to backup successfully.", latestFileName);

                            //string workspaceName = Path.GetFileNameWithoutExtension(latestFileName)
                            //    .Split('_')[0]
                            //    .Trim();

                            string workspaceName = folderName;

                            string procedure = "GetWorkspaceIdbyName";
                            var parameters = new Dictionary<string, object>
                        {
                            { "@WorkspaceName", workspaceName }
                        };
                            DataTable workspaceNameById = _dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);
                            int workspaceId = Convert.ToInt32(workspaceNameById.Rows[0]["workspace_info_id"]);

                            //string currentDate = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                            string fileNameWithDate = fileNameOnly;// Get the current date (without time)

                            // Read the file based on extension type (CSV or Excel)
                            List<dynamic> records = new List<dynamic>();
                            var fileExtension = Path.GetExtension(fileNameOnly).ToLower();

                            if (fileExtension == ".csv" || fileExtension == ".txt")
                            {
                                records = ReadCsvTxtStream(stream);
                            }
                            else if (fileExtension == ".xlsx" || fileExtension == ".xls")
                            {
                                records = ReadExcelStream(stream);
                            }

                            else
                            {
                                _logger.LogWarning("Unsupported file type: {FileExtension}", fileExtension);
                                continue;
                            }


                            int listIdCounter = Convert.ToInt32(_dbHandler.ExecuteScalar("SELECT ISNULL(MAX(list_id), 0) + 1 FROM ta_operator_contacts_trans"));
                            int contactIdCounter = 1;



                            DataTable bulkData = new DataTable();
                            bulkData.Columns.Add("list_id", typeof(int));
                            bulkData.Columns.Add("contact_id", typeof(int));
                            bulkData.Columns.Add("eventTimeStamp", typeof(string));
                            bulkData.Columns.Add("msisdn", typeof(string));
                            bulkData.Columns.Add("mcc", typeof(string));
                            bulkData.Columns.Add("mnc", typeof(string));
                            bulkData.Columns.Add("countryName", typeof(string));
                            bulkData.Columns.Add("created_date", typeof(DateTime));
                            bulkData.Columns.Add("createdby", typeof(string));
                            bulkData.Columns.Add("status", typeof(string));
                            bulkData.Columns.Add("workspace_id", typeof(int));
                            bulkData.Columns.Add("updated_date", typeof(DateTime));
                            bulkData.Columns.Add("fileName", typeof(string)); // New column for fileName


                            // ... Earlier part remains unchanged

                            List<DataRow> validRows = new List<DataRow>();
                            List<(DataRow Row, string ErrorMessage)> invalidRows = new List<(DataRow, string)>();
                            

                            foreach (var row in records)
                            {
                                var dictRow = row as IDictionary<string, object>;
                                var keyMap = dictRow.Keys.ToDictionary(k => k.ToLower(), k => k);

                                string GetSafeValue(string columnName) =>
                                    keyMap.ContainsKey(columnName.ToLower()) ? dictRow[keyMap[columnName.ToLower()]]?.ToString() : null;


                                DataRow newRow = bulkData.NewRow();
                                newRow["list_id"] = listIdCounter;
                                newRow["contact_id"] = contactIdCounter++;
                                string eventTimeStampValue = GetSafeValue("eventTimeStamp");
                                newRow["eventTimeStamp"] = !string.IsNullOrWhiteSpace(eventTimeStampValue)
                                    ? eventTimeStampValue
                                    : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                                newRow["msisdn"] = GetSafeValue("msisdn") ?? string.Empty;
                                newRow["mcc"] = GetSafeValue("mcc") ?? string.Empty;
                                newRow["mnc"] = GetSafeValue("mnc") ?? string.Empty;
                                newRow["countryName"] = GetSafeValue("countryName") ?? string.Empty;
                                newRow["created_date"] = DateTime.Now;
                                newRow["createdby"] = "S3";
                                newRow["status"] = "Updated";
                                newRow["workspace_id"] = workspaceId;
                                newRow["updated_date"] = DateTime.Now;
                                newRow["fileName"] = fileNameWithDate;


                                bulkData.Rows.Add(newRow);


                                if (!ValidateRow(row, out string errorMessage))
                                    invalidRows.Add((newRow, errorMessage));
                                else
                                    validRows.Add(newRow);
                            }

                            // Step 1: Insert ALL into ta_operator_contacts_trans
                            try
                            {
                                using (SqlConnection cnn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                                {
                                    cnn.Open();
                                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(cnn))
                                    {
                                        bulkCopy.DestinationTableName = "ta_operator_contacts_trans";
                                        foreach (DataColumn col in bulkData.Columns)
                                            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                                        bulkCopy.WriteToServer(bulkData);
                                    }
                                }
                                _logger.LogInformation("Inserted rows into ta_operator_contacts_trans");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error while inserting into ta_operator_contacts_trans");
                            }


                            // Step 2: Insert VALID rows into ta_operator_contacts
                            try
                            {
                                if (validRows.Any())
                                {
                                    DataTable validTable = bulkData.Clone();
                                    foreach (var row in validRows)
                                        validTable.ImportRow(row);

                                    using (SqlConnection cnn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                                    {
                                        cnn.Open();
                                        using (SqlBulkCopy bulkCopy = new SqlBulkCopy(cnn))
                                        {
                                            bulkCopy.DestinationTableName = "ta_operator_contacts";
                                            foreach (DataColumn col in validTable.Columns)
                                                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                                            bulkCopy.WriteToServer(validTable);
                                        }
                                    }
                                    _logger.LogInformation("Inserted rows into ta_operator_contacts");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error while inserting into ta_operator_contacts");
                            }

                            // Step 3: Insert INVALID rows into ta_operator_contacts_failure
                            try
                            {
                                if (invalidRows.Any())
                                {
                                    foreach (var (row, errorMessage) in invalidRows)
                                    {
                                        await InsertFailureAsync(row, errorMessage, workspaceId);
                                    }

                                    _logger.LogInformation("Inserted {Count} invalid rows into ta_operator_contacts_failure", invalidRows.Count);
                                }
                                else
                                {
                                    _logger.LogInformation("No invalid rows to insert for file: {FileName}", fileNameWithDate);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error while inserting into ta_operator_contacts_failure");
                            }






                            _logger.LogInformation("File data inserted successfully into the database.");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("No files found in the S3 bucket.");
                    }
                }



            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during file download and backup.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }

        _logger.LogInformation("File download and backup service is stopping.");
    }

}
