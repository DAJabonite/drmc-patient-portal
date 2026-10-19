FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source
COPY src/DrmcPatientPortal/DrmcPatientPortal.csproj src/DrmcPatientPortal/
RUN dotnet restore src/DrmcPatientPortal/DrmcPatientPortal.csproj -r linux-x64
COPY src/DrmcPatientPortal/ src/DrmcPatientPortal/
RUN dotnet publish src/DrmcPatientPortal/DrmcPatientPortal.csproj -c Release -r linux-x64 \
    --no-restore --self-contained false -p:UseAppHost=false -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
USER root
COPY scripts/setup-ocr-ubuntu.sh /tmp/setup-ocr-ubuntu.sh
RUN bash /tmp/setup-ocr-ubuntu.sh \
    && rm /tmp/setup-ocr-ubuntu.sh \
    && rm -rf /var/lib/apt/lists/* \
    && install -d -m 0700 -o app -g app /var/lib/drmc/keys /var/lib/drmc/storage \
       /var/lib/drmc/storage/patient-documents /var/lib/drmc/storage/temporary-uploads \
       /var/lib/drmc/storage/lab-reports /var/lib/drmc/storage/imports
WORKDIR /app
COPY --from=build /publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Ocr__NativeLibraryPath=/opt/drmc/ocr \
    Ocr__TessDataPath=/usr/share/tesseract-ocr/5/tessdata \
    LD_LIBRARY_PATH=/opt/drmc/ocr/x64
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "DrmcPatientPortal.dll"]
