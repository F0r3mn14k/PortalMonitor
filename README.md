# Portal Monitor

Niewielka aplikacja Windows do monitorowania portali HTTP/HTTPS oraz adresów IP/hostów z portem TCP.

## Funkcje

- automatyczna kontrola wszystkich pozycji co wybraną liczbę minut (1–1440) oraz przycisk kontroli ręcznej;
- adres URL, np. `https://example.com`, sprawdzany przez HTTP/HTTPS;
- IP lub nazwa hosta, np. `192.0.2.20`, sprawdzane przez ping; zaznaczenie „Sprawdź port TCP” uruchamia test wybranego portu;
- opis powodów niedostępności: DNS, timeout, odrzucone połączenie, niedostępna sieć/host, TLS i kod HTTP;
- dzienne logi obok programu: `LOG\rok\miesiąc\RRRR-MM-DD.log`, np. `LOG\2026\10\2026-10-15.log`;
- profile: zapis i wczytywanie oddzielnych list portali/IP; profile są w `%LOCALAPPDATA%\PortalMonitor\profiles.json`.
- wyraźne kolory stanu: zielony dla dostępnego celu oraz czerwony dla problemu.
- menu `⋮` na końcu każdego wiersza, obecnie z opcją bezpiecznego usunięcia celu.
- tabela dopasowuje szerokość kolumn do rozmiaru okna;
- działanie w zasobniku systemowym: zamknięcie okna ukrywa je, a monitorowanie działa dalej w tle.

## Zasobnik systemowy

Po uruchomieniu aplikacja normalnie pokazuje okno. Kliknięcie **X** ukrywa je w zasobniku systemowym Windows. Dwukrotnie kliknij ikonę, aby przywrócić okno. Kliknięcie prawym przyciskiem daje dostęp do poleceń **Otwórz Portal Monitor**, **Sprawdź teraz** oraz **Zakończ**; tylko ta ostatnia opcja faktycznie wyłącza program.

## Profile

W górnym pasku wybierz profil i użyj **Wczytaj profil** albo **Zapisz profil**. Aby utworzyć nowy, wpisz jego nazwę w pole „Nazwa nowego profilu” i wybierz **Nowy profil**. Nowy profil zaczyna jako pusty; po dodaniu adresów wybierz **Zapisz profil**. Lista jest też zapisywana automatycznie po dodaniu lub usunięciu adresu oraz przy zamknięciu aplikacji.

## Uruchomienie

```powershell
dotnet run --project .\PortalMonitor.csproj
```

## Publikacja do pojedynczego pliku EXE

```powershell
dotnet publish .\PortalMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

Plik wykonywalny będzie w `publish\PortalMonitor.exe`.
