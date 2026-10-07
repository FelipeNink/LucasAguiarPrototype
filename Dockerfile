# Build em dois estagios: o SDK (pesado) so existe para compilar; a imagem
# final leva apenas o runtime e o publicado.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# O csproj entra sozinho primeiro para o restore virar uma camada propria:
# enquanto as dependencias nao mudarem, o Docker reaproveita essa camada e
# nao baixa tudo de novo a cada build.
COPY LucasAguiar/LucasAguiar.csproj LucasAguiar/
RUN dotnet restore LucasAguiar/LucasAguiar.csproj

COPY LucasAguiar/ LucasAguiar/
RUN dotnet publish LucasAguiar/LucasAguiar.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Porta padrao. Plataformas que informam PORT sobrescrevem isto: o
# Program.cs le a variavel e reconfigura a escuta.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Nao roda como root.
USER $APP_UID

ENTRYPOINT ["dotnet", "LucasAguiar.dll"]
