import json
import os
import time
import undetected_chromedriver as uc
from selenium.webdriver import ActionChains
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC

import sys

def get_real_path(relative_path):
    """兼容 PyInstaller 打包后的资源路径"""
    if getattr(sys, 'frozen', False):
        # 如果是打包状态
        base_path = sys._MEIPASS
    else:
        # 正常脚本运行
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)



def check_new_grades(account_file='account.txt',
                     chrome_path="chrome-win64/chrome.exe",
                     driver_path="chromedriver-win64/chromedriver.exe",
                     headless=True):
    """
    检查是否有新增成绩
    Returns:
        added_courses (list[dict]): 新增课程信息列表
    """
    # === 配置浏览器 ===
    options = Options()
    if headless:
        options.add_argument('--headless')
    options.add_argument('--disable-gpu')
    options.add_argument('--no-sandbox')
    options.add_argument('--window-size=1920,1080')
    options.add_argument('--disable-blink-features=AutomationControlled')
    options.add_argument('--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36')

    chrome_path = get_real_path(chrome_path)
    driver_path = get_real_path(driver_path)

    driver = uc.Chrome(
        options=options,
        use_subprocess=True,
        browser_executable_path=chrome_path,
        driver_executable_path=driver_path,
        version_main=138
    )


    # === 登录 ===
    driver.get('https://eams.uestc.edu.cn/eams/teach/grade/course/person!search.action?semesterId=463&projectType=')
    account_file = get_real_path(account_file)
    with open(account_file, 'r', encoding='utf-8') as f:
        stdnum = f.readline().strip()
        stdpwd = f.readline().strip()


    WebDriverWait(driver, 10).until(EC.presence_of_element_located((By.ID, "username"))).send_keys(stdnum)
    time.sleep(0.5)

    password_element = WebDriverWait(driver, 10).until(EC.presence_of_element_located((By.ID, "password")))
    ActionChains(driver).move_to_element(password_element).perform()
    time.sleep(0.5)
    password_element.click()
    password_element.send_keys(stdpwd)

    login_button = WebDriverWait(driver, 10).until(EC.element_to_be_clickable((By.ID, "login_submit")))
    login_button.click()

    time.sleep(1)
    if "当前用户存在重复登录的情况" in driver.page_source:
        driver.get('https://eams.uestc.edu.cn/eams/teach/grade/course/person!search.action')

    driver.get('https://eams.uestc.edu.cn/eams/teach/grade/course/person!search.action?semesterId=463&projectType=')
    time.sleep(0.5)

    # === 获取成绩表 ===
    table = WebDriverWait(driver, 10).until(EC.presence_of_element_located((By.TAG_NAME, "table")))
    rows = table.find_elements(By.XPATH, ".//tbody/tr")
    grade_list = [[col.text.strip() for col in row.find_elements(By.TAG_NAME, "td")] for row in rows]

    headers = ["学年学期", "课程代码", "课程序号", "课程名称", "课程类别", "学分", "期末成绩", "总评成绩", "补考总评", "最终", "绩点"]
    grade_json = [dict(zip(headers, row)) for row in grade_list]

    added_courses = []
    if os.path.exists("grade.json"):
        with open("grade.json", "r", encoding="utf-8") as f:
            old_grade_json = json.load(f)
        old_courses = {course["课程代码"]: course for course in old_grade_json}
        new_courses = {course["课程代码"]: course for course in grade_json}
        added_courses = [course for code, course in new_courses.items() if code not in old_courses]
    else:
        added_courses = grade_json
    # 保存新成绩
    with open("grade.json", "w", encoding="utf-8") as f:
        json.dump(grade_json, f, ensure_ascii=False, indent=2)

    driver.quit()

    return added_courses
