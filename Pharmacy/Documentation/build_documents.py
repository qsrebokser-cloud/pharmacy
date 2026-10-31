from pathlib import Path
import sqlite3
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4, landscape
from docx import Document
from docx.shared import Inches, Pt, Cm
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'Documentation'
pdfmetrics.registerFont(TTFont('Candara', 'C:/Windows/Fonts/Candara.ttf'))
pdfmetrics.registerFont(TTFont('CandaraBold', 'C:/Windows/Fonts/Candarab.ttf'))
BLUE = colors.HexColor('#355CBD')
PALE = colors.HexColor('#D2DFFF')

def text(c, x, y, value, size=10, bold=False):
    c.setFillColor(colors.black)
    c.setFont('CandaraBold' if bold else 'Candara', size)
    c.drawString(x, y, value)

def arrow(c, points):
    c.setStrokeColor(BLUE)
    c.setLineWidth(1.3)
    for a, b in zip(points, points[1:]):
        c.line(*a, *b)
    x, y = points[-1]
    px, py = points[-2]
    from math import atan2, cos, sin, pi
    theta = atan2(y-py, x-px)
    for delta in (-.45, .45):
        c.line(x, y, x-7*cos(theta+delta), y-7*sin(theta+delta))

def er_diagram():
    c = canvas.Canvas(str(OUT / 'ER-diagram.pdf'), pagesize=landscape(A4))
    c.setTitle('ER-диаграмма базы данных Жар-птица')
    text(c, 30, 560, 'База данных ОАО «Жар-птица»', 23, True)
    text(c, 30, 538, '3НФ · SQLite · PK — первичный ключ · FK — внешний ключ · UQ — уникальность', 11)
    def table(x,y,w,title,fields):
        h=31+len(fields)*15
        c.setFillColor(colors.white)
        c.setStrokeColor(BLUE)
        c.rect(x,y-h,w,h,fill=1)
        c.setFillColor(PALE)
        c.rect(x,y-27,w,27,fill=1)
        text(c,x+9,y-18,title,13,True)
        for i,f in enumerate(fields):
            text(c,x+9,y-44-i*15,f,9)
    table(30,500,175,'Roles',['PK Id','UQ Name'])
    table(30,365,175,'Users',['PK Id','FullName','UQ Login','PasswordHash','Email','FK RoleId → Roles.Id'])
    table(303,500,210,'Orders',['PK Id','FK UserId → Users.Id','Address','FK StatusId → OrderStatuses.Id','CreatedAt','DeliveryAt'])
    table(600,500,208,'OrderStatuses',['PK Id','UQ Name'])
    table(303,270,210,'OrderItems',['PK, FK OrderId → Orders.Id','PK, FK ProductId → Products.Id','Quantity','UnitPriceCents'])
    table(600,360,208,'Products',['PK Id','Name','FK TypeId → ProductTypes.Id','Description','Composition','PriceCents','Discount','DiscountActive','MinStock','Stock','ImagePath'])
    table(30,180,175,'ProductTypes',['PK Id','UQ Name'])
    arrow(c,[(115,439),(115,365)])
    text(c,125,405,'1 : N',10,True)
    arrow(c,[(205,330),(250,330),(250,465),(303,465)])
    text(c,215,440,'1 : N',10,True)
    arrow(c,[(600,465),(513,465)])
    text(c,540,475,'1 : N',10,True)
    arrow(c,[(408,379),(408,270)])
    text(c,418,316,'1 : N',10,True)
    arrow(c,[(600,220),(513,220)])
    text(c,540,230,'1 : N',10,True)
    arrow(c,[(205,140),(555,140),(555,323),(600,323)])
    text(c,360,150,'1 : N',10,True)
    text(c,30,48,'Цена хранится в копейках. Итоги вычисляются по позициям. Цена в заказе сохраняется на момент оформления.',10)
    text(c,30,31,'Удаление заказа каскадно удаляет позиции; удаление товара из истории заказов запрещено внешним ключом.',10)
    c.save()

def algorithm():
    c = canvas.Canvas(str(OUT / 'Algorithm.pdf'), pagesize=A4)
    c.setTitle('Алгоритм работы приложения Жар-птица')
    text(c,35,790,'Алгоритм работы приложения',22,True)
    text(c,35,768,'Условные обозначения по ГОСТ 19.701-90',11)
    def block(x,y,w,h,label,kind='process'):
        c.setFillColor(PALE)
        c.setStrokeColor(BLUE)
        c.setLineWidth(1)
        if kind=='end':
            c.roundRect(x,y,w,h,h/2,fill=1)
        elif kind=='decision':
            p=c.beginPath()
            p.moveTo(x+w/2,y+h)
            p.lineTo(x+w,y+h/2)
            p.lineTo(x+w/2,y)
            p.lineTo(x,y+h/2)
            p.close()
            c.drawPath(p,fill=1)
        elif kind=='io':
            p=c.beginPath()
            p.moveTo(x+14,y+h)
            p.lineTo(x+w,y+h)
            p.lineTo(x+w-14,y)
            p.lineTo(x,y)
            p.close()
            c.drawPath(p,fill=1)
        else:
            c.rect(x,y,w,h,fill=1)
        lines=label.split('\n')
        c.setFont('Candara',10)
        c.setFillColor(colors.black)
        for i,line in enumerate(lines):
            c.drawCentredString(x+w/2,y+h/2+(len(lines)-1)*6-i*12-3,line)
    block(210,729,175,30,'Начало','end')
    block(180,664,235,42,'Открыть БД; при первом запуске\nимпортировать исходные данные')
    block(180,595,235,44,'Вывести каталог гостя;\nпоиск, фильтр, сортировка','io')
    block(222,505,151,66,'Вход выбран?','decision')
    block(410,518,150,42,'Просмотр каталога','io')
    block(180,441,235,42,'Ввести логин и пароль','io')
    block(222,351,151,66,'Данные верны?','decision')
    block(30,363,140,42,'Сообщение об ошибке','io')
    block(180,283,235,43,'Определить роль; показать ФИО\nи разрешённые действия')
    block(180,211,235,43,'Каталог / корзина / управление\nпродукцией и заказами по роли')
    block(222,123,151,65,'Выход?','decision')
    block(210,55,175,30,'Завершение','end')
    for top,bottom in [(729,706),(664,639),(595,571),(505,483),(441,417),(351,326),(283,254),(211,188),(123,85)]:
        arrow(c,[(297,top),(297,bottom)])
    arrow(c,[(373,538),(410,538)])
    text(c,380,548,'Нет',10)
    arrow(c,[(485,560),(485,617),(415,617)])
    text(c,306,490,'Да',10)
    arrow(c,[(222,384),(170,384)])
    text(c,185,395,'Нет',10)
    arrow(c,[(100,405),(100,462),(180,462)])
    text(c,306,334,'Да',10)
    arrow(c,[(373,156),(465,156),(465,232),(415,232)])
    text(c,390,164,'Нет',10)
    text(c,306,100,'Да',10)
    c.showPage()
    text(c,35,790,'Оформление заказа из корзины',22,True)
    text(c,35,768,'Транзакция обеспечивает целостность заказа и складских остатков',11)
    block(210,725,175,30,'Начало','end')
    block(180,650,235,45,'Добавить товары в корзину;\nввести адрес доставки','io')
    block(210,541,175,76,'Корзина и адрес\nзаполнены?','decision')
    block(180,468,235,44,'Начать транзакцию;\nповторно проверить остатки')
    block(210,356,175,78,'Товары доступны?','decision')
    block(180,267,235,57,'Создать заказ и позиции;\nсохранить цены; уменьшить остатки;\nзафиксировать транзакцию')
    block(180,184,235,44,'Очистить корзину;\nпоказать номер заказа','io')
    block(210,105,175,30,'Конец','end')
    block(22,557,145,45,'Ошибка ввода;\nсохранить корзину','io')
    block(22,371,145,45,'Откат транзакции;\nсообщить об остатках','io')
    for top,bottom in [(725,695),(650,617),(541,512),(468,434),(356,324),(267,228),(184,135)]:
        arrow(c,[(297,top),(297,bottom)])
    arrow(c,[(210,579),(167,579)])
    text(c,179,590,'Нет',10)
    arrow(c,[(94,602),(94,671),(180,671)])
    text(c,306,524,'Да',10)
    arrow(c,[(210,395),(167,395)])
    text(c,179,406,'Нет',10)
    arrow(c,[(94,416),(15,416),(15,671),(180,671)])
    text(c,306,340,'Да',10)
    c.showPage()
    text(c,35,790,'Алгоритм разработки приложения',22,True)
    text(c,35,768,'Последовательность выполнения экзаменационного задания',11)
    block(210,725,175,30,'Начало','end')
    block(155,646,285,48,'Изучить предметную область,\nроли пользователей и руководство по стилю')
    block(155,564,285,48,'Спроектировать БД в 3НФ;\nсоздать таблицы, первичные и внешние ключи')
    block(155,482,285,48,'Подготовить и импортировать данные;\nпроверить связи и суммы; построить ER-диаграмму')
    block(155,400,285,48,'Создать WPF-интерфейс; подключить БД;\nреализовать авторизацию и права ролей')
    block(155,318,285,48,'Реализовать каталог, поиск, фильтр, сортировку,\nкорзину и управление продукцией и заказами')
    block(210,206,175,78,'Проверки пройдены?','decision')
    block(22,222,145,46,'Исправить ошибки;\nповторить проверку')
    block(155,127,285,48,'Подготовить сборку, SQL, схемы,\nскриншоты и исходники для репозитория')
    block(210,55,175,30,'Конец','end')
    for top,bottom in [(725,694),(646,612),(564,530),(482,448),(400,366),(318,284),(206,175),(127,85)]:
        arrow(c,[(297,top),(297,bottom)])
    arrow(c,[(210,245),(167,245)])
    text(c,179,255,'Нет',10)
    arrow(c,[(94,268),(94,342),(155,342)])
    text(c,306,185,'Да',10)
    c.save()

def report():
    d=Document()
    section=d.sections[0]
    section.page_width=Cm(21)
    section.page_height=Cm(29.7)
    section.top_margin=Cm(1.8)
    section.bottom_margin=Cm(1.8)
    section.left_margin=Cm(2)
    section.right_margin=Cm(2)
    normal=d.styles['Normal']
    normal.font.name='Candara'
    normal.font.size=Pt(11)
    for name in ['Title','Heading 1','Heading 2']:
        d.styles[name].font.name='Candara'
        d.styles[name].font.color.rgb=__import__('docx').shared.RGBColor(0,0,0)
        ppr=d.styles[name].element.get_or_add_pPr()
        border=OxmlElement('w:pBdr')
        for edge in ['top','left','bottom','right','between']:
            item=OxmlElement('w:'+edge)
            item.set(qn('w:val'),'nil')
            border.append(item)
        ppr.append(border)
    d.add_heading('Проверка приложения Жар-птица',0)
    d.add_paragraph('Демонстрационный экзамен 2026 · Вариант 5 · 09.02.07',style='Subtitle')
    d.add_paragraph('Разработано настольное приложение C# WPF с базой данных SQLite. Проверены импорт исходных данных, авторизация и разграничение прав, работа с продукцией, корзиной и заказами. Автоматические проверки завершились успешно.')
    table=d.add_table(rows=1,cols=2)
    table.style='Table Grid'
    table.rows[0].cells[0].text='Проверяемая область'
    table.rows[0].cells[1].text='Результат'
    for a,b in [
        ('Импорт','8 пользователей, 15 товаров, 7 заказов, 12 позиций'),
        ('Целостность БД','PRAGMA integrity_check = ok; ошибок внешних ключей нет'),
        ('Авторизация','Верные данные принимаются; неверный пароль и SQL-инъекция отклоняются'),
        ('Разграничение прав','Гость не оформляет заказ; пользователь не изменяет продукцию'),
        ('Корзина','Пустой адрес и нехватка товара сохраняют корзину; неуспешный заказ откатывается'),
        ('Оформление','Скидка рассчитана в копейках; остаток уменьшается; корзина очищается'),
        ('Продукция','Добавление с ID max + 1, изменение цены и удаление проверены'),
        ('Хранение','После повторного открытия изменения сохранены; импорт не дублируется')]:
        cells=table.add_row().cells
        cells[0].text=a
        cells[1].text=b
    d.add_paragraph('Снимки ниже получены встроенным рендерингом реальных WPF-окон запущенного приложения. Полный протокол 24 автоматических проверок находится в Deliverable/checks.txt.')
    captures=ROOT/'Deliverable'/'captures'
    pages=[
        ('01-guest','Каталог гостя','В главном меню видны логотип, поиск, выбор типа продукции и сортировка стоимости. Товары со скидкой более 10% выделяются цветом #BB5FCA.'),
        ('02-customer','Авторизованный пользователь','В правом верхнем углу отображаются ФИО и роль. Доступны корзина, подробная информация и добавление товара.'),
        ('03-cart','Корзина','Товар сохраняет оформление каталога. Доступны удаление единицы товара, ввод адреса, оформление всей корзины и возврат в каталог.'),
        ('04-admin','Интерфейс администратора','Доступны управление продукцией и просмотр заказов. Кнопка выхода возвращает пользователя в гостевой режим.'),
        ('05-editor','Редактор продукции','Данные товара загружаются из базы. ID доступен только для чтения. Предусмотрены фото, название, тип, стоимость, описание, состав, скидка и остатки.'),
        ('06-orders','Заказы','Список показывает покупателя, адрес, статус, даты и состав заказа. Администратору доступны добавление, редактирование и удаление.')]
    for file,title,description in pages:
        d.add_page_break()
        d.add_heading(title,1)
        d.add_paragraph(description)
        d.add_picture(str(captures/(file+'.png')),width=Cm(16.8))
    d.save(OUT/'Verification.docx')

if __name__=='__main__':
    er_diagram()
    algorithm()
    import sys
    if '--pdf-only' in sys.argv:
        sys.exit(0)
    report()
    with sqlite3.connect(ROOT/'Deliverable'/'pharmacy.db') as db:
        (ROOT/'DatabaseWithData.sql').write_text('PRAGMA foreign_keys=OFF;\n'+'\n'.join(db.iterdump())+'\nPRAGMA foreign_keys=ON;\nPRAGMA foreign_key_check;\n',encoding='utf-8')
    print('Created ER-diagram.pdf, Algorithm.pdf, Verification.docx, DatabaseWithData.sql')
