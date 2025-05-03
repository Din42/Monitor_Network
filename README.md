![Диаграмма последовательности для сценария LogTcpConnection](https://github.com/user-attachments/assets/6d1d0017-bb07-42ad-b642-f61b81fbf6f1)

В этой диаграмме последовательности показан сценарий вызова метода LogTcpConnection() по таймеру и его внутренняя логика. Разберём её по частям:
________________________________________
1.	Участники (Participants)
Слева направо у нас четыре «ролика» (lifelines):
o	Timer — генерирует событие «Tick» каждые 10 секунд.
o	Network — ваш главный класс, который обрабатывает это событие.
o	Provider (TcpTableProvider) — утилитный класс, который через PInvoke достаёт TCP-таблицу и резолвит IP→домен.
o	Database — компонент, отвечающий за запись посещений в БД.
________________________________________
2.	Событие таймера
Timer -> Network: Tick event
activate Network
Таймер посылает Tick event в объект Network, после чего его «жизненная линия» становится активной (толстая вертикальная полоса).
________________________________________
3.	Запрос TCP-таблицы
Network -> Provider: GetExtendedTcpTable()
activate Provider
Provider --> Network: List<MIB_TCPROW_OWNER_PID>
deactivate Provider
Network обращается к Provider, запрашивая текущие TCP-соединения; Provider возвращает список записей о соединениях и сразу «завершает» свою активность (deactivate).
________________________________________
4.	Цикл по всем записям
loop for each row
  alt remotePort == 80 or 443
    …
  end
end
Фрагмент loop означает, что внутри будет повторяться набор действий для каждой записи из списка.
________________________________________
6.	Условие фильтрации (alt-фрагмент)
alt remotePort == 80 or 443
  Network -> Provider: GetDomainFromIp(ip)
  activate Provider
  Provider --> Network: domain or ip
  deactivate Provider
  Network -> Database: LogVisit(domain, processName, ip)
end
________________________________________
	alt — альтернативный путь, он же «if».
o	Если порт удалённого конца соединения 80 или 443 (HTTP/HTTPS), то:
1.	Network снова вызывает Provider для преобразования IP в домен.
2.	Provider возвращает доменное имя (или сам IP, если резолв не удался).
3.	Network посылает команду LogVisit(...) в Database, чтобы сохранить запись.
6.	Возврат управления
________________________________________

Network --> Timer: Return
deactivate Network
По окончании цикла и всех действий Network возвращает управление таймеру и «закрывает» свою активность.



    ![Диаграмма классов](https://github.com/user-attachments/assets/f7bc8521-8a23-4e43-b7ae-8738d4182ef7)
  	
Классы
Внутри пакета описаны четыре класса:
•	Network
Это главный класс, который:
o	Имеет поля (- означает private):
	Logger logger — логирование.
	NotifyIcon netAct — иконка в трее.
	Icon netTx, netRx, netTxRx, netIdle — разные иконки активности сети.
	Thread netActMonWorker — рабочий поток для мониторинга сети.
o	И имеет методы (+ означает public):
	Network() — конструктор.
	ShowMainForm() — показать главное окно.
	LogTcpConnection() — логирование TCP-соединений.
	BlockSelectedIp(), UnblockSelectedIp() — управление блокировкой IP.
________________________________________
•	Database
Статический класс, который:
o	LogVisit(string domain, string processName, string ip) — логирует сетевое соединение.
o	GetNetworkHistory() — возвращает историю соединений в виде таблицы.
________________________________________
•	FirewallRuleManager (отмечен как <<utility>>)
Утилитный класс:
o	RunNetshCommand(string arguments) — запускает команды для настройки файрвола (netsh).
________________________________________
•	TcpTableProvider (тоже <<utility>>)
Утилитный класс для работы с TCP:
o	GetExtendedTcpTable(...) — получить список TCP-соединений.
o	GetDomainFromIp(string ip) — получить домен по IP.
o	IsPrivateIP(IPAddress ip) — проверить, является ли IP приватным.
________________________________________
 Связи между классами
•	Network --> Database : uses
Network использует Database для записи сетевой активности.
•	Network --> FirewallRuleManager : manages rules
Network управляет правилами файрвола через FirewallRuleManager.
•	Network --> TcpTableProvider : retrieves TCP info
Network запрашивает информацию о TCP через TcpTableProvider.
________________________________________
Как читать всю диаграмму?
1.	Network — главный актор. Он координирует всю работу.
2.	Для выполнения задач:
	Он получает TCP-таблицу через TcpTableProvider.  
	Он пишет логи в базу данных через Database.  
	Он добавляет или удаляет правила файрвола через FirewallRuleManager.  
3.	FirewallRuleManager и TcpTableProvider — утилитные (вспомогательные) классы, у них нет состояния (состояние не хранится между вызовами).




