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





