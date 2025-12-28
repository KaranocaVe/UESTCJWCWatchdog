import json
import os
import sys
import time

import undetected_chromedriver as uc
from selenium.webdriver import ActionChains
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import WebDriverWait


def get_real_path(relative_path):
    """兼容 PyInstaller 打包后的资源路径"""
    if getattr(sys, 'frozen', False):
        # 如果是打包状态
        base_path = sys._MEIPASS
    else:
        # 正常脚本运行
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)


def create_driver_and_login(stdnum=None,
                            stdpwd=None,
                            chrome_path="chrome-win64/chrome.exe",
                            driver_path="chromedriver-win64\\chromedriver.exe",
                            headless=True,
                            dev=False):
    """
    创建浏览器并登录
    Returns:
        driver : 新建浏览器对象
    """
    # === 配置浏览器 ===
    options = Options()
    if headless and not (dev):
        options.add_argument('--headless')
    options.add_argument('--disable-gpu')
    options.add_argument('--no-sandbox')
    options.add_argument('--window-size=1920,1080')
    options.add_argument('--disable-blink-features=AutomationControlled')
    options.add_argument(
        '--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36')

    chrome_path = get_real_path(chrome_path)
    driver_path = get_real_path(driver_path)

    if dev:
        driver = uc.Chrome(
            options=options,
            use_subprocess=True,
            version_main=138
        )
    else:
        driver = uc.Chrome(
            options=options,
            use_subprocess=True,
            browser_executable_path=chrome_path,
            driver_executable_path=driver_path,
            version_main=138
        )

    # === 登录 ===
    driver.get('https://eams.uestc.edu.cn/eams/teach/grade/course/person!search.action?semesterId=463&projectType=')

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
    driver.refresh()
    return driver


def get_final_grades(driver, semister):
    """
    检查是否有新增成绩
    Returns:
        added_courses (list[dict]): 新增课程信息列表
    """
    driver.get(
        'https://eams.uestc.edu.cn/eams/teach/grade/course/person!search.action?semesterId=%s&projectType=' % str(
            semister))
    time.sleep(0.5)

    # === 获取成绩表 ===
    table = WebDriverWait(driver, 10).until(EC.presence_of_element_located((By.TAG_NAME, "table")))
    rows = table.find_elements(By.XPATH, ".//tbody/tr")
    grade_list = [[col.text.strip() for col in row.find_elements(By.TAG_NAME, "td")] for row in rows]

    headers = ["学年学期", "课程代码", "课程序号", "课程名称", "课程类别", "学分", "期末成绩", "总评成绩", "补考总评",
               "最终", "绩点"]
    grade_json = [dict(zip(headers, row)) for row in grade_list]

    return grade_json


def get_usual_grades(driver, semister):
    """
    获取平时成绩
    Returns:
        usual_grades (list[dict]): 平时成绩信息列表
    """
    driver.add_cookie({'name': 'semester.id', 'value': str(semister)})
    driver.get("https://eams.uestc.edu.cn/eams/teach/grade/usual/usual-grade-std.action")
    time.sleep(0.5)

    # === 获取平时成绩表 ===
    table = WebDriverWait(driver, 10).until(EC.presence_of_element_located((By.CSS_SELECTOR, "table.gridtable")))
    rows = table.find_elements(By.XPATH, ".//tbody/tr")
    usual_grade_list = [[col.text.strip() for col in row.find_elements(By.TAG_NAME, "td")][:7] for row in rows]

    headers = ["学年学期", "课程代码", "课程序号", "课程名称", "课程类别", "学分", "平时成绩"]
    usual_grades_json = [dict(zip(headers, row)) for row in usual_grade_list]

    return usual_grades_json


def main():
    # 通过命令行参数读入账号密码
    if len(sys.argv) < 4:
        print("用法: python cli.py <学号> <密码> <学期ID>")
        sys.exit(1)
    stdnum = sys.argv[1]
    stdpwd = sys.argv[2]
    semester_id = sys.argv[3]

    driver = create_driver_and_login(dev=False, stdnum=stdnum, stdpwd=stdpwd)
    final_grades = []
    try:
        final_grades = get_final_grades(driver=driver, semister=semester_id)
    except Exception as e:
        print(f"查询最终成绩出错：{e}", file=sys.stderr)
        driver.quit()
        sys.exit(1)

    try:
        usual_grades = get_usual_grades(driver=driver, semister=semester_id)
    except Exception as e:
        print(f"查询平时成绩出错：{e}", file=sys.stderr)
        driver.quit()
        sys.exit(1)

    # 合并平时成绩和期末成绩
    result = {
        "usual_grades": usual_grades,
        "final_grades": final_grades
    }

    print(json.dumps(result, ensure_ascii=False))  # 关键行：输出为合法 JSON

    driver.quit()


if __name__ == "__main__":
    main()
